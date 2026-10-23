"""Real MAVLink framing tests over loopback UDP and a virtual serial port."""
import os
import socket
import time
import unittest
from pymavlink import mavutil
from gateway import TelemetryState

class TransportTests(unittest.TestCase):
    def packet(self):
        encoder=mavutil.mavlink.MAVLink(None,srcSystem=7,srcComponent=1)
        return encoder.heartbeat_encode(2,3,128,4,4).pack(encoder)
    def verify(self, message):
        self.assertIsNotNone(message)
        state=TelemetryState(7)
        self.assertTrue(state.accept(message,mavutil.mode_string_v10))
        self.assertTrue(state.sample({'sourceId':'test','uavId':'u','missionId':'m'})['armed'])
    def test_udp_mavlink_frame(self):
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as probe:
            probe.bind(('127.0.0.1',0));port=probe.getsockname()[1]
        connection=mavutil.mavlink_connection('udpin:127.0.0.1:%d'%port)
        try:
            with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sender:
                sender.sendto(self.packet(),('127.0.0.1',port))
            self.verify(connection.recv_match(type='HEARTBEAT',blocking=True,timeout=3))
        finally:connection.close()
    @unittest.skipUnless(os.name=='posix','Virtual serial PTY requires POSIX')
    def test_usb_serial_mavlink_frame(self):
        import pty
        master,slave=pty.openpty()
        connection=None
        try:
            connection=mavutil.mavlink_connection(os.ttyname(slave),baud=57600)
            os.write(master,self.packet())
            self.verify(connection.recv_match(type='HEARTBEAT',blocking=True,timeout=3))
        finally:
            if connection:connection.close()
            os.close(master);os.close(slave)

if __name__=='__main__':unittest.main()
