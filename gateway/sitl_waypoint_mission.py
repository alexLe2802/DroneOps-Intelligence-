#!/usr/bin/env python3
"""Exercise the production gateway against an owned, isolated Copter simulator.

No endpoint argument exists: only the TCP port of the process started here is
used. Never reads sessions, opens serial ports or connects to the real router.
"""
import argparse
import datetime as dt
import json
from pathlib import Path
import subprocess
import tempfile
import time

from pymavlink import mavutil
from flight_control import ControlBridge, FlightControl, same_item


def run(binary, defaults):
    binary = Path(binary).resolve(strict=True)
    defaults = Path(defaults).resolve(strict=True)
    instance = 11
    port = 5760 + 10 * instance
    # Reject an occupied port before launching, so an existing FC proxy cannot
    # accidentally be used in place of the child simulator.
    import socket
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', port))
    with tempfile.TemporaryDirectory(prefix='droneops-waypoint-sitl-') as work:
        simulation_defaults = Path(work) / 'copter.parm'
        # The gateway countdown uses wall time. At 5x simulation speed its ten
        # seconds consume fifty simulated seconds, so use a simulator-only
        # auto-disarm window that exceeds that countdown. No real FC writes.
        simulation_defaults.write_text(defaults.read_text() + '\nDISARM_DELAY 60\n')
        with open(Path(work) / 'sitl.log', 'wb') as log:
            process = subprocess.Popen([str(binary), '-w', '-S', '-I', str(instance),
                '--home', '-35.363261,149.165230,584,353', '--model', '+',
                '--speedup', '5', '--defaults', str(simulation_defaults)],
                cwd=work, stdout=log, stderr=subprocess.STDOUT)
            link = None
            try:
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline and process.poll() is None:
                    try:
                        link = mavutil.mavlink_connection(f'tcp:127.0.0.1:{port}',
                            source_system=255, source_component=190)
                        if link.wait_heartbeat(timeout=2):
                            break
                        link.close()
                        link = None
                    except OSError:
                        time.sleep(.2)
                if link is None or process.poll() is not None:
                    raise RuntimeError('Owned simulator failed to start.')
                bridge = ControlBridge()
                control = FlightControl(1, 1, bridge, enabled=True)
                control.tick(link)
                envelope = dict(fcParameterVerificationRequired=True, configureRecovery=False,
                    cruiseSpeedMetersPerSecond=3, maxSpeedMetersPerSecond=5,
                    limits=dict(policyVersion='multicopter-envelope-v1',
                        takeoffSpeedMetersPerSecond=1, climbSpeedMetersPerSecond=1,
                        descentSpeedMetersPerSecond=.7, landingSpeedMetersPerSecond=.7,
                        maxAltitudeMeters=30))
                stage, command, plan = 'AwaitingFcProfile', None, None
                last_poll = last_requests = last_rc = 0
                ready_at = None
                started = time.monotonic()
                seen_airborne = False
                reached, current, statuses, protocol = set(), set(), [], []
                max_height = 0
                result = dict(simulator='ArduCopter', realAircraftTouched=False,
                    simulationSpeedup=5, simulationDisarmDelaySeconds=60)
                while time.monotonic() - started < 180:
                    now = time.monotonic()
                    if process.poll() is not None:
                        raise RuntimeError('Simulator exited during mission.')
                    if now - last_requests > 3:
                        for msgid in (0, 1, 24, 33, 42, 245):
                            link.mav.command_long_send(1, 1, 511, 0, msgid, 200000, 0, 0, 0, 0, 0)
                        for msgid in (148, 242):
                            link.mav.command_long_send(1, 1, 512, 0, msgid, 0, 0, 0, 0, 0, 0)
                        last_requests = now
                    if now - last_rc > .5:
                        link.mav.rc_channels_override_send(1, 1, 1500, 1500, 1000, 1500, 1800, 1500, 1500, 1500)
                        last_rc = now
                    packet = link.recv_match(blocking=True, timeout=.02)
                    if packet is not None:
                        if packet.get_type().startswith('MISSION_'):
                            protocol.append(packet.to_dict())
                        if packet.get_type() == 'MISSION_ITEM_INT' and packet.seq < len(control.items):
                            if not same_item(control.items[packet.seq], packet):
                                protocol.append({'expected': control.items[packet.seq]})
                        control.accept(packet, link)
                        kind = packet.get_type()
                        if kind == 'STATUSTEXT':
                            statuses.append(packet.text)
                        elif kind == 'GLOBAL_POSITION_INT':
                            max_height = max(max_height, packet.relative_alt / 1000)
                        elif kind == 'MISSION_ITEM_REACHED':
                            reached.add(packet.seq)
                        elif kind == 'MISSION_CURRENT':
                            current.add(packet.seq)
                    evidence = control.evidence()
                    if evidence['firmwareParameters']:
                        version = evidence['firmwareParameters']['firmwareVersion']
                        if version != '4.7.1':
                            raise RuntimeError(f'Expected Copter 4.7.1, got {version}')
                        result['firmwareVersion'] = version
                    if now - last_poll > .3:
                        utc = dt.datetime.now(dt.timezone.utc)
                        response = dict(enabled=True, leaseSeconds=3, stage=stage,
                            serverNow=utc.isoformat(), compiledHash='isolated-waypoint-test',
                            requiredEnvelopeHash='isolated-waypoint-test', operatingEnvelope=envelope,
                            command=command, mission=plan)
                        bridge.responses.put((bridge.instance, now, response))
                        last_poll = now
                    control.tick(link)
                    if command and bridge.report and bridge.report['commandId'] == command['id']:
                        if not bridge.report['succeeded']:
                            raise RuntimeError(f"{command['action']} failed: {statuses[-8:]}; evidence={evidence}; uploaded={control.uploaded_hash}; envelope={control.envelope.error}; protocol={protocol[-8:]}")
                        action = command['action']
                        result[action] = True
                        command = None
                        if action == 'Upload':
                            stage = 'Ready'
                            ready_at = now
                            link.set_mode(5)  # LOITER on the owned simulator only.
                        elif action == 'Arm':
                            stage = 'Countdown'
                        elif action == 'Start':
                            stage = 'Running'
                    if command is None:
                        action = None
                        if stage == 'AwaitingFcProfile' and control.envelope.verified_hash:
                            home = control.fresh('HOME_POSITION', 15)
                            def item(seq, cmd, lat, alt):
                                return dict(seq=seq, command=cmd, frame=3, param1=0, param2=2 if cmd == 16 else 0,
                                    param3=0, param4=None, x=lat, y=home.longitude, z=alt)
                            plan = dict(firmwareTarget='ArduPilot', operationalEnvelope=envelope,
                                items=[item(0, 22, home.latitude, 15),
                                       dict(seq=1, command=178, frame=2, param1=1, param2=3,
                                            param3=-1, param4=0, x=0, y=0, z=0),
                                       item(2, 16, home.latitude + 1500, 15),
                                       item(3, 16, home.latitude + 3000, 15),
                                       item(4, 21, home.latitude + 3000, 0)])
                            action, stage = 'Upload', 'Uploading'
                        elif stage == 'Ready' and evidence['preArmReady'] and now - ready_at >= 5:
                            hb = control.fresh('HEARTBEAT')
                            if hb and hb.custom_mode == 5:
                                action, stage = 'Arm', 'Arming'
                        elif stage == 'Countdown' and control.arm_at and now - control.arm_at >= 10:
                            action, stage = 'Start', 'Starting'
                        if action:
                            utc = dt.datetime.now(dt.timezone.utc)
                            command = dict(id=action, action=action,
                                expiresAt=(utc + dt.timedelta(seconds=60 if action == 'Upload' else 8)).isoformat())
                    if stage == 'Running':
                        seen_airborne |= evidence['landedState'] == 2 and max_height > 5
                        if seen_airborne and evidence['landedState'] == 1 and evidence['armed'] is False:
                            if not {3, 4}.issubset(reached):
                                raise RuntimeError(f'Arrival waypoints were not confirmed: {sorted(reached)}')
                            result.update(airborne=True, landedAndDisarmed=True, reachedItems=sorted(reached),
                                currentItems=sorted(current), maximumRelativeAltitudeMeters=max_height,
                                uploadReadbackVerified=True, extendedRecoveryConfigured=False)
                            print(json.dumps(result))
                            return
                raise RuntimeError(f'Timeout stage={stage}; evidence={evidence}; status={statuses[-12:]}')
            except Exception:
                print((Path(work) / 'sitl.log').read_text(errors='replace')[-3000:])
                raise
            finally:
                if link is not None:
                    link.close()
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', required=True)
    parser.add_argument('--defaults', required=True)
    args = parser.parse_args()
    run(args.binary, args.defaults)
