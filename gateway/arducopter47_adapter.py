"""ArduCopter 4.7 parameter adapter, initially exercised only against SITL.

The synchronous apply_to_sitl helper accepts only an isolated simulator link.
Runtime parameter mapping uses the nonblocking envelope verifier. This module
never invents battery thresholds or landing sites.
It cannot produce a flight authorization/verifiedEnvelopeHash by itself.
"""
import math
import time


class ProfileError(ValueError):
    pass


def parameter_targets(envelope, current):
    """Convert approved multicopter policy to ArduCopter 4.7 native units."""
    if envelope.get('fcParameterVerificationRequired') is not True:
        raise ProfileError('Envelope must explicitly require FC verification.')
    limits = envelope.get('limits') or {}
    if limits.get('policyVersion') not in ('multicopter-envelope-v1', 'multicopter-envelope-v2'):
        raise ProfileError('Unsupported policy version.')
    def number(value, name, minimum, maximum):
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not minimum <= value <= maximum:
            raise ProfileError(f'{name} is outside the approved profile.')
        return float(value)
    v2 = limits['policyVersion'] == 'multicopter-envelope-v2'
    horizontal_cap, climb_cap, descent_cap, landing_cap = (15, 8, 6, 1.5) if v2 else (5, 1.5, 1, .7)
    cruise = number(envelope.get('cruiseSpeedMetersPerSecond'), 'cruise', .5, horizontal_cap)
    maximum = number(envelope.get('maxSpeedMetersPerSecond'), 'maximum speed', cruise, horizontal_cap)
    takeoff = number(limits.get('takeoffSpeedMetersPerSecond'), 'takeoff', 1, climb_cap)
    climb = number(limits.get('climbSpeedMetersPerSecond'), 'climb', takeoff, climb_cap)
    descent = number(limits.get('descentSpeedMetersPerSecond'), 'descent', .6, descent_cap)
    landing = number(limits.get('landingSpeedMetersPerSecond'), 'landing', .6, min(landing_cap, descent))
    ceiling = number(limits.get('maxAltitudeMeters'), 'altitude ceiling', 10.1, 30)
    if takeoff != climb:
        raise ProfileError('ArduCopter 4.7 AUTO takeoff and climb share WP_SPD_UP; use equal limits until phase-specific enforcement is proven.')
    profile = limits.get('recoveryEnergyProfile') or {}
    if not isinstance(current, dict):
        raise ProfileError('Fresh ArduCopter parameter snapshot is required.')
    # A calibrated energy model is optional for native RTL; preserve the FC's
    # existing return height only if it already fits the approved envelope.
    rtl_alt = number(profile.get('returnAltitudeMeters', current.get('RTL_ALT_M')), 'RTL altitude', 1, ceiling)
    for name in ('FENCE_TYPE', 'FS_OPTIONS', 'BATT_MONITOR', 'BATT_CAPACITY',
                 'BATT_LOW_VOLT', 'BATT_CRT_VOLT', 'BATT_LOW_MAH', 'BATT_CRT_MAH',
                 'BATT_FS_LOW_ACT', 'BATT_FS_CRT_ACT'):
        if name not in current or not isinstance(current[name], (int, float)) or not math.isfinite(current[name]):
            raise ProfileError(f'Missing FC parameter {name}.')
    if current['BATT_MONITOR'] == 0 or current['BATT_CAPACITY'] <= 0:
        raise ProfileError('Battery monitor/capacity is not configured on FC.')
    if current['BATT_LOW_VOLT'] <= 0 and current['BATT_LOW_MAH'] <= 0:
        raise ProfileError('Low battery threshold must be calibrated before applying flight limits.')
    if current['BATT_CRT_VOLT'] <= 0 and current['BATT_CRT_MAH'] <= 0:
        raise ProfileError('Critical battery threshold must be calibrated before applying flight limits.')
    if current['BATT_FS_LOW_ACT'] == 0 or current['BATT_FS_CRT_ACT'] == 0:
        raise ProfileError('Low/critical battery actions must be configured before applying flight limits.')
    if current['BATT_FS_LOW_ACT'] != 2 or current['BATT_FS_CRT_ACT'] != 1:
        raise ProfileError('This adapter requires reviewed low-battery RTL and critical-battery LAND actions.')
    for low, critical in (('BATT_LOW_VOLT', 'BATT_CRT_VOLT'), ('BATT_LOW_MAH', 'BATT_CRT_MAH')):
        if current[low] < 0 or current[critical] < 0:
            raise ProfileError('Battery thresholds cannot be negative.')
        if current[low] > 0 and current[critical] > 0 and current[critical] >= current[low]:
            raise ProfileError('Critical battery threshold must be lower than low battery threshold.')
    if any(current[name] > current['BATT_CAPACITY'] for name in ('BATT_LOW_MAH', 'BATT_CRT_MAH')):
        raise ProfileError('Remaining-capacity thresholds exceed configured FC capacity; actual pack capacity still requires verification.')
    if int(current['FENCE_TYPE']) != current['FENCE_TYPE'] or int(current['FS_OPTIONS']) != current['FS_OPTIONS']:
        raise ProfileError('FC bitmask values must be integers.')
    # Clear continuation on RC/GCS loss, keep independent landing/gripper bits.
    options = int(current['FS_OPTIONS']) & ~23
    return {
        'WP_SPD': maximum, 'WP_SPD_UP': climb, 'WP_SPD_DN': descent,
        'LAND_SPD_MS': landing, 'RTL_SPEED_MS': maximum, 'RTL_ALT_M': rtl_alt,
        'FENCE_ALT_MAX': ceiling, 'FENCE_TYPE': int(current['FENCE_TYPE']) | 1,
        'FENCE_ACTION': 1, 'FENCE_ENABLE': 1,
        'FS_THR_ENABLE': 1, 'FS_GCS_ENABLE': 1, 'FS_OPTIONS': options,
    }


def apply_to_sitl(link, targets, *, system=1, component=1, timeout=3, clock=time.monotonic):
    """Set and independently read back each parameter on an isolated SITL link.

    The function deliberately has a SITL name and must never be called from the
    production gateway. Every call is bounded and rejects mismatched sources.
    """
    for name, wanted in targets.items():
        link.mav.param_set_send(system, component, name.encode('ascii'), float(wanted), 9)
        deadline = clock() + timeout
        accepted = False
        while clock() < deadline:
            packet = link.recv_match(type='PARAM_VALUE', blocking=True, timeout=min(.2, max(0, deadline-clock())))
            if packet is None or packet.get_srcSystem() != system or packet.get_srcComponent() != component:
                continue
            received_name = packet.param_id.decode('ascii', 'ignore') if isinstance(packet.param_id, bytes) else packet.param_id
            if received_name.split('\0')[0] == name and math.isclose(packet.param_value, wanted, rel_tol=0, abs_tol=1e-4):
                accepted = True
                break
        if not accepted:
            raise ProfileError(f'FC did not ACK parameter {name}.')
        link.mav.param_request_read_send(system, component, name.encode('ascii'), -1)
        deadline = clock() + timeout
        verified = False
        while clock() < deadline:
            packet = link.recv_match(type='PARAM_VALUE', blocking=True, timeout=min(.2, max(0, deadline-clock())))
            if packet is None or packet.get_srcSystem() != system or packet.get_srcComponent() != component:
                continue
            received_name = packet.param_id.decode('ascii', 'ignore') if isinstance(packet.param_id, bytes) else packet.param_id
            if received_name.split('\0')[0] == name:
                verified = math.isfinite(packet.param_value) and math.isclose(packet.param_value, wanted, rel_tol=0, abs_tol=1e-4)
                break
        if not verified:
            raise ProfileError(f'FC read-back mismatch for {name}.')
    return True
