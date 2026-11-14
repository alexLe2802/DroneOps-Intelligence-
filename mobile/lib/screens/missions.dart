import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../core/api.dart';
import '../core/display.dart';
import 'shared.dart';

class MissionsScreen extends StatefulWidget {
  final ApiClient api;
  const MissionsScreen({super.key, required this.api});
  @override
  State<MissionsScreen> createState() => _MissionsScreenState();
}

class _MissionsScreenState extends State<MissionsScreen> {
  String search = '';
  late Future<List<Json>> future;
  @override
  void initState() {
    super.initState();
    future = widget.api.missions();
  }

  Future<void> refresh() async {
    setState(() => future = widget.api.missions());
    await future;
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Padding(
        padding: const EdgeInsets.all(16),
        child: TextField(
          decoration: const InputDecoration(
            labelText: 'Search own missions',
            prefixIcon: Icon(Icons.search),
          ),
          onChanged: (v) => setState(() => search = v),
        ),
      ),
      Expanded(
        child: FutureBuilder<List<Json>>(
          future: future,
          builder: (context, snapshot) {
            if (snapshot.hasError) {
              return ErrorPanel(error: snapshot.error!, retry: refresh);
            }
            if (!snapshot.hasData) {
              return const Center(child: CircularProgressIndicator());
            }
            final list = snapshot.data!
                .where(
                  (m) => '${m['name']} ${m['status']} ${m['uavCode']}'
                      .toLowerCase()
                      .contains(search.toLowerCase()),
                )
                .toList();
            return RefreshIndicator(
              onRefresh: refresh,
              child: pageList([
                if (list.isEmpty) notice('No matching missions.'),
                ...list.map(
                  (m) => Card(
                    child: ListTile(
                      title: Text(text(m['name'])),
                      subtitle: Text(
                        '${text(m['status'])} · ${text(m['uavCode'])}\n${stamp(m['startTime'])}',
                      ),
                      isThreeLine: true,
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => open(
                        context,
                        'Mission',
                        MissionScreen(api: widget.api, id: m['id']),
                      ),
                    ),
                  ),
                ),
              ]),
            );
          },
        ),
      ),
    ],
  );
}

class MissionScreen extends StatelessWidget {
  final ApiClient api;
  final String id;
  const MissionScreen({super.key, required this.api, required this.id});
  @override
  Widget build(BuildContext context) => LoadView(
    load: () async {
      final m = await api.mission(id);
      final pending = object(m['pendingUpdate']);
      return pageList([
        title(text(m['name'])),
        field('Status', m['status']),
        field('Description', m['description']),
        field('UAV', m['uavCode']),
        field('Start', stamp(m['startTime'])),
        field('End', stamp(m['endTime'])),
        if (pending.isNotEmpty)
          JsonEvidence(
            value: pending,
            heading: 'Proposed update / review evidence',
          ),
        FilledButton.tonal(
          onPressed: () => open(
            context,
            'Validation & decision',
            ValidationScreen(api: api, id: id),
          ),
          child: const Text('View validation & decision'),
        ),
        OutlinedButton(
          onPressed: () => open(
            context,
            'Review history',
            MissionReviewHistory(api: api, id: id),
          ),
          child: const Text('View approval / rejection history'),
        ),
        RouteView(
          points: records(m['waypoints'] ?? []),
          altitudeReference: m['altitudeReference']?.toString(),
        ),
        notice(
          'Viewing a mission or a passed validation does not establish flight clearance. Planning, submission and Manager decisions remain in the Web portal.',
        ),
      ]);
    },
  );
}

class MissionReviewHistory extends StatelessWidget {
  final ApiClient api;
  final String id;
  const MissionReviewHistory({super.key, required this.api, required this.id});
  @override
  Widget build(BuildContext context) => LoadView(
    load: () async {
      final entries = records(
        await api.request('GET', 'missions/${ApiClient.identity(id)}/history'),
      );
      return pageList([
        notice(
          'Recorded review decisions and reasons. A previous approval does not approve a newer proposal.',
        ),
        if (entries.isEmpty) notice('No recorded review history.'),
        ...entries.map(
          (entry) =>
              JsonEvidence(value: entry, heading: 'Review request / decision'),
        ),
      ]);
    },
  );
}

class ValidationScreen extends StatelessWidget {
  final ApiClient api;
  final String id;
  const ValidationScreen({super.key, required this.api, required this.id});
  @override
  Widget build(BuildContext context) => LoadView(
    load: () async {
      final result = await api.request(
        'GET',
        'missions/${ApiClient.identity(id)}/validation',
      );
      return pageList([
        JsonEvidence(value: result, heading: 'Persisted validation'),
        notice(
          'Check the run timestamp, stale state and proposal identity. Historical validation is evidence, not permission to launch.',
        ),
      ]);
    },
  );
}

class RouteView extends StatelessWidget {
  final List<Json> points;
  final String? altitudeReference;
  const RouteView({super.key, required this.points, this.altitudeReference});
  @override
  Widget build(BuildContext context) {
    final valid = points
        .where(
          (p) =>
              p['latitude'] is num &&
              p['longitude'] is num &&
              (p['latitude'] as num).abs() <= 90 &&
              (p['longitude'] as num).abs() <= 180,
        )
        .toList();
    final coordinates = valid
        .map(
          (p) => LatLng(
            (p['latitude'] as num).toDouble(),
            (p['longitude'] as num).toDouble(),
          ),
        )
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        title(
          'Route · ${altitudeReference ?? 'altitude reference unavailable'}',
        ),
        if (coordinates.isNotEmpty)
          SizedBox(
            height: 260,
            child: FlutterMap(
              options: MapOptions(
                initialCenter: coordinates.first,
                initialZoom: 14,
              ),
              children: [
                TileLayer(
                  urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                  userAgentPackageName: 'click.droneops.droneops_mobile',
                ),
                PolylineLayer(
                  polylines: [
                    Polyline(
                      points: coordinates,
                      strokeWidth: 4,
                      color: Colors.cyan,
                    ),
                  ],
                ),
                const RichAttributionWidget(
                  attributions: [
                    TextSourceAttribution('OpenStreetMap contributors'),
                  ],
                ),
              ],
            ),
          ),
        if (coordinates.isEmpty)
          notice('Route unavailable. No valid positions.'),
        ...points.map(
          (p) => field(
            'Waypoint ${text(p['sequenceOrder'])}',
            '${text(p['latitude'])}, ${text(p['longitude'])} · ${measurement(p['altitude'], 'm')} · ${text(p['actionType'])}',
          ),
        ),
      ],
    );
  }
}
