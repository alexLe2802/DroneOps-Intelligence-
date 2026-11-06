"""Nonblocking ArduCopter 4.7.1 parameter verification on the reader thread.

Enabled only by an execution-enabled backend lease and an opted-in gateway.
The receipt proves parameter readback, never battery calibration or airworthiness.
"""
import math

from arducopter47_adapter import ProfileError, parameter_targets


class EnvelopeVerifier:
    def __init__(self, system, component, clock):
        self.system, self.component, self.clock = system, component, clock
        self.reset()

    def reset(self):
        self.plan_hash = None
        self.verified_hash = None
        self.targets = {}
        self.pending = []
        self.phase = None
        self.error = None
        self.failed_hash = None
        self.observed = {}
        self.guards = {}

    def begin(self, plan_hash, envelope, current, link):
        if plan_hash == self.failed_hash:
            return
        try:
            targets = parameter_targets(envelope, current)
        except ProfileError as error:
            self.error = str(error)
            return
        self.plan_hash, self.targets = plan_hash, targets
        self.guards = {name: value for name, value in current.items()
                       if name.startswith('BATT_') or name in ('ARMING_CHECK', 'ARMING_SKIPCHK', 'FS_EKF_ACTION')}
        # Apply enable switches after thresholds and recovery actions.
        self.pending = sorted(targets, key=lambda key: key == 'FENCE_ENABLE')
        self.observed = {}
        self.verified_hash = None
        self.error = None
        self.started = self.clock()
        self._next(link)

    def _next(self, link):
        if not self.pending:
            self.verified_hash = self.plan_hash
            self.phase = None
            return
        self.name = self.pending[0]
        self.phase = 'ack'
        self.sent = self.clock()
        link.mav.param_set_send(self.system, self.component, self.name.encode(), float(self.targets[self.name]), 9)

    def fail(self, reason):
        self.verified_hash = None
        self.pending = []
        self.phase = None
        self.error = reason
        self.failed_hash = self.plan_hash

    def accept(self, message, link):
        if message.get_type() != 'PARAM_VALUE' or message.get_srcSystem() != self.system or message.get_srcComponent() != self.component:
            return
        name = message.param_id.decode('ascii', 'ignore') if isinstance(message.param_id, bytes) else message.param_id
        name = name.split('\0')[0]
        if name in self.guards and (not math.isfinite(message.param_value) or not math.isclose(message.param_value, self.guards[name], abs_tol=1e-4, rel_tol=0)):
            self.fail(f'FC battery/arming configuration changed: {name}')
            return
        if name not in self.targets:
            return
        matches = math.isfinite(message.param_value) and math.isclose(message.param_value, self.targets[name], abs_tol=1e-4, rel_tol=0)
        if self.verified_hash:
            if not matches:
                self.fail(f'FC parameter changed: {name}')
            else:
                self.observed[name] = self.clock()
            return
        if not self.phase or name != self.name:
            return
        if not matches:
            self.fail(f'FC rejected parameter: {name}')
        elif self.phase == 'ack':
            self.phase = 'read'
            self.sent = self.clock()
            link.mav.param_request_read_send(self.system, self.component, name.encode(), -1)
        else:
            self.observed[name] = self.clock()
            self.pending.pop(0)
            self._next(link)

    def tick(self):
        if self.phase and (self.clock() - self.sent > 3 or self.clock() - self.started > 90):
            self.fail('FC parameter ACK/readback timed out.')
        if self.verified_hash and any(self.clock() - seen > 60 for seen in self.observed.values()):
            self.fail('FC envelope readback expired.')
