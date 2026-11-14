import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/display.dart';
import 'shared.dart';
import 'missions.dart';

class HistoryScreen extends StatefulWidget {
  final ApiClient api;
  const HistoryScreen({super.key, required this.api});
  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  int page = 1;
  DateTime? from, to;
  String status = '', uav = '';
  List<Json> aircraft = [];
  String? aircraftError;
  @override
  void initState() {
    super.initState();
    loadAircraft();
  }

  Future<void> loadAircraft() async {
    try {
      final all = <Json>[];
      int p = 1, total = 1;
      do {
        final data = await widget.api.request(
          'GET',
          'uavs/myUAV',
          query: {'pageNumber': '$p', 'pageSize': '100'},
        );
        all.addAll(records(data));
        total = data is List
            ? 1
            : (object(data)['totalPages'] as num?)?.toInt() ?? 1;
        p++;
      } while (p <= total && mounted);
      if (mounted) setState(() => aircraft = all);
    } catch (e) {
      if (mounted) setState(() => aircraftError = e.toString());
    }
  }

  String day(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
  Future<void> date(bool start) async {
    final d = await showDatePicker(
      context: context,
      initialDate: (start ? from : to) ?? DateTime.now(),
      firstDate: DateTime(2020),
      lastDate: DateTime.now().add(const Duration(days: 365)),
    );
    if (d != null && mounted) {
      setState(() {
        if (start) {
          from = d;
        } else {
          to = d;
        }
        page = 1;
      });
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Padding(
        padding: const EdgeInsets.all(12),
        child: Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            OutlinedButton(
              onPressed: () => date(true),
              child: Text(from == null ? 'From (UTC)' : day(from!)),
            ),
            OutlinedButton(
              onPressed: () => date(false),
              child: Text(to == null ? 'To (UTC)' : day(to!)),
            ),
            TextButton(
              onPressed: () => setState(() {
                from = null;
                to = null;
                status = '';
                uav = '';
                page = 1;
              }),
              child: const Text('Reset'),
            ),
            DropdownButton<String>(
              value: status,
              items: ['', 'Completed', 'Aborted', 'Failed', 'Cancelled']
                  .map(
                    (s) => DropdownMenuItem(
                      value: s,
                      child: Text(s.isEmpty ? 'All outcomes' : s),
                    ),
                  )
                  .toList(),
              onChanged: (v) => setState(() {
                status = v!;
                page = 1;
              }),
            ),
            DropdownButton<String>(
              value: uav,
              items: [
                const DropdownMenuItem(value: '', child: Text('All UAVs')),
                ...aircraft.map(
                  (a) => DropdownMenuItem(
                    value: text(a['id']),
                    child: Text(text(a['code'])),
                  ),
                ),
              ],
              onChanged: (v) => setState(() {
                uav = v!;
                page = 1;
              }),
            ),
          ],
        ),
      ),
      if (aircraftError != null) Text('UAV filter unavailable: $aircraftError'),
      Expanded(
        child: from != null && to != null && from!.isAfter(to!)
            ? const Center(child: Text('From date must not follow To date.'))
            : LoadView(
                key: ValueKey('$page-$from-$to-$uav-$status'),
                load: () async {
                  final data = object(
                    await widget.api.request(
                      'GET',
                      'flight-history',
                      query: {
                        'pageNumber': '$page',
                        'pageSize': '20',
                        if (from != null) 'fromDate': '${day(from!)}T00:00:00Z',
                        if (to != null) 'toDate': '${day(to!)}T23:59:59.999Z',
                        if (uav.isNotEmpty) 'uavId': uav,
                        if (status.isNotEmpty) 'status': status,
                      },
                    ),
                  );
                  final list = records(data);
                  return pageList([
                    notice(
                      'Date filters use UTC start dates. Times display GMT+7. Missing reports or telemetry do not establish a safe flight.',
                    ),
                    if (list.isEmpty) notice('No matching records.'),
                    ...list.map(
                      (f) => Card(
                        child: ListTile(
                          title: Text(text(f['missionName'])),
                          subtitle: Text(
                            '${text(f['uavCode'])} · ${text(f['status'])}\nReport: ${text(f['reportStatus'])} · ${stamp(f['startTime'])}',
                          ),
                          isThreeLine: true,
                          onTap: () => open(
                            context,
                            'Flight report',
                            ReportScreen(api: widget.api, id: f['missionId']),
                          ),
                        ),
                      ),
                    ),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        TextButton(
                          onPressed: page > 1
                              ? () => setState(() => page--)
                              : null,
                          child: const Text('Previous'),
                        ),
                        Text('Page $page'),
                        TextButton(
                          onPressed:
                              page <
                                  ((data['totalPages'] as num?)?.toInt() ?? 1)
                              ? () => setState(() => page++)
                              : null,
                          child: const Text('Next'),
                        ),
                      ],
                    ),
                  ]);
                },
              ),
      ),
    ],
  );
}

class ReportScreen extends StatefulWidget {
  final ApiClient api;
  final String id;
  const ReportScreen({super.key, required this.api, required this.id});
  @override
  State<ReportScreen> createState() => _ReportScreenState();
}

class _ReportScreenState extends State<ReportScreen> {
  Json? selected;
  @override
  Widget build(BuildContext context) => LoadView(
    key: ValueKey(selected?['reportId']),
    load: () async {
      final path = 'flight-history/${ApiClient.identity(widget.id)}';
      final current = object(await widget.api.request('GET', '$path/report'));
      final revisions = records(
        await widget.api.request('GET', '$path/report/revisions'),
      );
      final r = selected ?? current;
      List<Json>? telemetry, incidents;
      String? warning;
      try {
        telemetry = records(await widget.api.request('GET', '$path/telemetry'));
      } catch (_) {
        warning = 'Telemetry could not be loaded; no count can be inferred.';
      }
      try {
        incidents = records(await widget.api.request('GET', '$path/incidents'));
      } catch (_) {
        warning = '${warning ?? ''} Incident source unavailable.';
      }
      final route = object(current['executionRoute']);
      return pageList([
        title(text(r['missionName'])),
        field(
          'Generation / revision',
          '${text(r['generationStatus'])} / ${text(r['revision'])}',
        ),
        if (warning != null) notice(warning),
        DropdownButtonFormField<String>(
          initialValue: r['reportId']?.toString(),
          isExpanded: true,
          decoration: const InputDecoration(
            labelText: 'Immutable report revision',
          ),
          items: revisions
              .map(
                (v) => DropdownMenuItem(
                  value: v['reportId'].toString(),
                  child: Text(
                    'Revision ${text(v['revision'])} · ${text(v['generationStatus'])}',
                  ),
                ),
              )
              .toList(),
          onChanged: (id) {
            setState(
              () => selected = revisions.firstWhere((v) => v['reportId'] == id),
            );
          },
        ),
        JsonEvidence(value: r, heading: 'Report evidence'),
        if (route.isNotEmpty) ...[
          field('Execution plan hash', route['planHash']),
          RouteView(
            points: records(route['waypoints'] ?? []),
            altitudeReference: route['altitudeReference'],
          ),
        ] else
          notice(
            'Immutable planned route unavailable. The current editable mission is not substituted.',
          ),
        if (telemetry != null) ...[
          title('Recorded flight path'),
          RouteView(points: telemetry, altitudeReference: 'Recorded telemetry'),
          title('Latest recorded samples'),
          ...telemetry.reversed
              .take(8)
              .map(
                (p) => field(
                  stamp(p['recordedAt']),
                  '${text(p['latitude'])}, ${text(p['longitude'])} · ${measurement(p['altitude'], 'm')} · ${measurement(p['batteryLevel'], '%')}',
                ),
              ),
        ],
        title('Linked incidents'),
        if (incidents != null)
          ...incidents.map(
            (i) => JsonEvidence(value: i, heading: text(i['title'])),
          ),
      ]);
    },
  );
}
