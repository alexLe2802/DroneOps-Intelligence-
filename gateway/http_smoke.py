"""Run a loopback MAVLink -> Jetson gateway -> WebSocket smoke test. Pairing code is supplied on stdin."""
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
from pymavlink import mavutil

server=sys.argv[1]
code=sys.stdin.readline().strip()
with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as probe:
    probe.bind(('127.0.0.1',0));port=probe.getsockname()[1]
with tempfile.TemporaryDirectory() as folder:
    session=Path(folder)/'session.json'
    command=[sys.executable,str(Path(__file__).with_name('gateway.py')),'--server',server,'--device','udpin:127.0.0.1:%d'%port,
             '--system-id','1','--pair-code-stdin','--session-file',str(session)]
    process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    try:
        process.stdin.write(code+'\n');process.stdin.flush();process.stdin.close()
        mav=mavutil.mavlink.MAVLink(None,srcSystem=1,srcComponent=1)
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as sender:
            for i in range(12):
                packets=[mav.heartbeat_encode(2,3,128,4,4),
                         mav.autopilot_version_encode(0,0,0,0,0,[0]*8,[0]*8,[0]*8,1,1,123456789),
                         mav.global_position_int_encode(1000*i,113500000,1063400000,60000,50000,300,400,0,9000),
                         mav.sys_status_encode(0,0,0,0,12000,0,70,0,0,0,0,0,0),
                         mav.mission_current_encode(0)]
                for packet in packets:
                    sender.sendto(packet.pack(mav),('127.0.0.1',port));mav.seq=(mav.seq+1)%256
                time.sleep(1)
        if not session.exists():raise RuntimeError('Gateway did not identify and pair the drone over WebSocket')
        if process.poll() is not None:raise RuntimeError('Gateway exited during telemetry forwarding')
        print('Real MAVLink frames were forwarded over UDP to the paired gateway.')
    finally:
        process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
