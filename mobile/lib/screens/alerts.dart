import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/display.dart';
import 'shared.dart';

class AlertsScreen extends StatelessWidget {
  final ApiClient api;
  const AlertsScreen({super.key, required this.api});
  @override
  Widget build(BuildContext context) => LoadView(
    load: () async {
      final feed = object(
        await api.request(
          'GET',
          'notifications',
          query: {'unreadOnly': 'false', 'limit': '50'},
        ),
      );
      final list = records(feed);
      return pageList([
        title('${text(feed['unreadCount'])} unread alerts'),
        notice(
          'In-app alerts refresh when you open or pull to refresh this page. Background push notifications are not enabled.',
        ),
        FilledButton.tonal(
          onPressed: () =>
              open(context, 'Incident registry', IncidentList(api: api)),
          child: const Text('Open incident registry'),
        ),
        if (list.isEmpty) notice('No notifications.'),
        ...list.map(
          (n) => Card(
            child: ListTile(
              leading: Icon(
                n['isRead'] == true
                    ? Icons.notifications_none
                    : Icons.notifications_active,
              ),
              title: Text(text(n['title'])),
              subtitle: Text('${text(n['message'])}\n${stamp(n['createdAt'])}'),
              isThreeLine: true,
              onTap: () =>
                  open(context, 'Alert', NotificationScreen(api: api, item: n)),
            ),
          ),
        ),
      ]);
    },
  );
}

class NotificationScreen extends StatefulWidget {
  final ApiClient api;
  final Json item;
  const NotificationScreen({super.key, required this.api, required this.item});
  @override
  State<NotificationScreen> createState() => _NotificationScreenState();
}

class _NotificationScreenState extends State<NotificationScreen> {
  bool busy = false, read = false;
  String? error;
  @override
  Widget build(BuildContext context) => pageList([
    title(text(widget.item['title'])),
    field('Message', widget.item['message']),
    field('Created', stamp(widget.item['createdAt'])),
    if (error != null) notice(error!),
    FilledButton.tonal(
      onPressed: busy || read || widget.item['isRead'] == true
          ? null
          : () async {
              setState(() => busy = true);
              try {
                await widget.api.request(
                  'PATCH',
                  'notifications/${ApiClient.identity(widget.item['id'])}/read',
                );
                if (mounted) setState(() => read = true);
              } catch (e) {
                if (mounted) setState(() => error = e.toString());
              } finally {
                if (mounted) setState(() => busy = false);
              }
            },
      child: Text(
        read || widget.item['isRead'] == true ? 'Read' : 'Mark as read',
      ),
    ),
    if (widget.item['referenceType'] == 'Incident' &&
        widget.item['referenceId'] is String)
      FilledButton(
        onPressed: () => open(
          context,
          'Incident',
          IncidentScreen(api: widget.api, id: widget.item['referenceId']),
        ),
        child: const Text('View incident'),
      ),
  ]);
}

class IncidentList extends StatefulWidget {
  final ApiClient api;
  const IncidentList({super.key, required this.api});
  @override
  State<IncidentList> createState() => _IncidentListState();
}

class _IncidentListState extends State<IncidentList> {
  int page = 1;
  String status = '';
  @override
  Widget build(BuildContext context) => Column(
    children: [
      DropdownButton<String>(
        value: status,
        items: ['', 'Open', 'Acknowledged', 'Resolved']
            .map(
              (s) => DropdownMenuItem(
                value: s,
                child: Text(s.isEmpty ? 'All incident statuses' : s),
              ),
            )
            .toList(),
        onChanged: (v) => setState(() {
          status = v!;
          page = 1;
        }),
      ),
      Expanded(
        child: LoadView(
          key: ValueKey('$page-$status'),
          load: () async {
            final data = object(
              await widget.api.request(
                'GET',
                'incidents',
                query: {
                  'pageNumber': '$page',
                  'pageSize': '20',
                  if (status.isNotEmpty) 'status': status,
                },
              ),
            );
            final list = records(data);
            return pageList([
              if (list.isEmpty) notice('No matching incidents.'),
              ...list.map(
                (i) => Card(
                  child: ListTile(
                    title: Text(text(i['title'])),
                    subtitle: Text(
                      '${text(i['severity'])} · ${text(i['status'])}\n${stamp(i['createdAt'])}',
                    ),
                    isThreeLine: true,
                    onTap: () => open(
                      context,
                      'Incident',
                      IncidentScreen(api: widget.api, id: i['id']),
                    ),
                  ),
                ),
              ),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  TextButton(
                    onPressed: page > 1 ? () => setState(() => page--) : null,
                    child: const Text('Previous'),
                  ),
                  Text('Page $page'),
                  TextButton(
                    onPressed: list.length == 20
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

class IncidentScreen extends StatelessWidget {
  final ApiClient api;
  final String id;
  const IncidentScreen({super.key, required this.api, required this.id});
  @override
  Widget build(BuildContext context) => LoadView(
    load: () async {
      final data = await api.request(
        'GET',
        'incidents/${ApiClient.identity(id)}',
      );
      final history = await api.request(
        'GET',
        'incidents/${ApiClient.identity(id)}/history',
      );
      return pageList([
        JsonEvidence(value: data, heading: 'Incident evidence'),
        title('Recorded history'),
        ...records(history)
            .map((h) => JsonEvidence(value: h, heading: 'Transition')),
      ]);
    },
  );
}
