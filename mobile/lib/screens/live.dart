import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/display.dart';
import '../core/polling.dart';
import 'shared.dart';
import 'missions.dart';

class LiveScreen extends StatefulWidget {
  final ApiClient api;
  const LiveScreen({super.key, required this.api});
  @override
  State<LiveScreen> createState() => _LiveScreenState();
}

class _LiveScreenState extends State<LiveScreen> with WidgetsBindingObserver {
  late ForegroundPoller<List<Json>> poller;
  List<Json>? missions;
  Object? error;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    poller = ForegroundPoller(
      read: () async =>
          records(await widget.api.request('GET', 'live/missions')),
      onData: (v) {
        if (mounted) {
          setState(() {
            missions = v;
            error = null;
          });
        }
      },
      onError: (e) {
        if (mounted) setState(() => error = e);
      },
      interval: const Duration(seconds: 10),
    );
    poller.start();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      poller.start();
    } else {
      poller.pause();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    poller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (missions == null) {
      if (error != null) {
        return ErrorPanel(error: error!, retry: poller.refresh);
      }
      return const Center(child: CircularProgressIndicator());
    }
    return pageList([
      if (error != null)
        notice(
          'Live list unavailable: $error. Previously loaded records are not live.',
        ),
      if (missions!.isEmpty)
        notice('No active flights in your authorized scope.'),
      ...missions!.map(
        (m) => Card(
          child: ListTile(
            title: Text(text(m['missionName'])),
            subtitle: Text(
              '${text(m['uavCode'])} · ${text(m['connectivityStatus'])}\nLast seen ${stamp(m['lastSeen'])}',
            ),
            isThreeLine: true,
            onTap: () {
              poller.pause();
              Navigator.of(context)
                  .push(
                    MaterialPageRoute<void>(
                      builder: (_) => Scaffold(
                        appBar: AppBar(title: Text(text(m['missionName']))),
                        body: TelemetryScreen(
                          api: widget.api,
                          id: m['missionId'],
                        ),
                      ),
                    ),
                  )
                  .then((_) {
                    if (mounted) poller.start();
                  });
            },
          ),
        ),
      ),
    ]);
  }
}

class TelemetryScreen extends StatefulWidget {
  final ApiClient api;
  final String id;
  const TelemetryScreen({super.key, required this.api, required this.id});
  @override
  State<TelemetryScreen> createState() => _TelemetryScreenState();
}

class _TelemetryScreenState extends State<TelemetryScreen>
    with WidgetsBindingObserver {
  late ForegroundPoller<Json> poller;
  Json? sample;
  Object? error;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    poller = ForegroundPoller(
      read: () async => object(
        await widget.api.request(
          'GET',
          'missions/${ApiClient.identity(widget.id)}/telemetry/latest',
        ),
      ),
      onData: (v) {
        if (mounted) {
          setState(() {
            sample = v;
            error = null;
          });
        }
      },
      onError: (e) {
        if (mounted) setState(() => error = e);
      },
    );
    poller.start();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      poller.start();
    } else {
      poller.pause();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    poller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (sample == null) {
      if (error != null) {
        return ErrorPanel(error: error!, retry: poller.refresh);
      }
      return const Center(child: CircularProgressIndicator());
    }
    final s = sample!;
    final valid =
        error == null &&
        s['missionId'] == widget.id &&
        fresh(s, DateTime.now());
    final batteryTime = DateTime.tryParse(
      s['batteryObservedAt']?.toString() ?? '',
    );
    final batteryValid =
        valid &&
        batteryTime != null &&
        DateTime.now().toUtc().difference(batteryTime.toUtc()).inSeconds <=
            ((s['staleAfterSeconds'] as num?)?.toInt() ?? 10) &&
        !batteryTime.toUtc().isAfter(
          DateTime.now().toUtc().add(const Duration(seconds: 5)),
        );
    return pageList([
      notice(
        valid
            ? 'Online · fresh mapped sample'
            : '${text(s['connectivityStatus'])} · NOT LIVE. Values below are last recorded evidence; do not use them as current flight state.',
      ),
      if (error != null) notice('Update failed: $error'),
      field('UAV / source', '${text(s['uavCode'])} / ${text(s['sourceId'])}'),
      field('Source timestamp', stamp(s['sourceTimestamp'])),
      field('Received at', stamp(s['receivedAt'])),
      field(
        valid ? 'Battery' : 'Last recorded battery',
        measurement(s['batteryPercent'], '%', valid: batteryValid),
      ),
      field('Battery observed', stamp(s['batteryObservedAt'])),
      field(
        'Voltage',
        measurement(s['batteryVoltageVolts'], 'V', valid: batteryValid),
      ),
      field(
        'Altitude (${text(s['altitudeReference'])})',
        measurement(s['altitudeMeters'], 'm'),
      ),
      field(
        'Ground speed',
        measurement(s['groundSpeedMetersPerSecond'], 'm/s'),
      ),
      field(
        'Armed state',
        s['armed'] == null
            ? 'Unknown'
            : s['armed'] == true
            ? 'Armed'
            : 'Disarmed',
      ),
      field('Flight mode', s['flightMode']),
      field('Mission progress', measurement(s['progressPercent'], '%')),
      if (s['isValid'] == true)
        RouteView(
          points: [
            {
              'sequenceOrder': s['missionSequence'],
              'latitude': s['latitude'],
              'longitude': s['longitude'],
              'altitude': s['altitudeMeters'],
            },
          ],
          altitudeReference: s['altitudeReference'],
        ),
      FilledButton.tonal(
        onPressed: poller.refresh,
        child: const Text('Refresh telemetry'),
      ),
      notice(
        'Monitoring only. This app does not arm the aircraft, bypass failsafes or infer flight completion from an ACK.',
      ),
    ]);
  }
}
