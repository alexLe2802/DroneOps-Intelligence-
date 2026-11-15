import 'package:flutter/material.dart';

import '../core/api.dart';
import '../core/display.dart';

class LoadView extends StatefulWidget {
  final Future<Widget> Function() load;
  const LoadView({super.key, required this.load});
  @override
  State<LoadView> createState() => _LoadViewState();
}

class _LoadViewState extends State<LoadView> {
  late Future<Widget> future;
  @override
  void initState() {
    super.initState();
    future = widget.load();
  }

  @override
  void didUpdateWidget(LoadView old) {
    super.didUpdateWidget(old);
    if (old.key != widget.key) future = widget.load();
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Widget>(
    future: future,
    builder: (context, s) {
      if (s.hasError) {
        return ErrorPanel(
          error: s.error!,
          retry: () => setState(() => future = widget.load()),
        );
      }
      if (!s.hasData) return const Center(child: CircularProgressIndicator());
      return RefreshIndicator(
        onRefresh: () async {
          setState(() => future = widget.load());
          await future;
        },
        child: s.data!,
      );
    },
  );
}

class ErrorPanel extends StatelessWidget {
  final Object error;
  final VoidCallback retry;
  const ErrorPanel({super.key, required this.error, required this.retry});
  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(error.toString(), textAlign: TextAlign.center),
          const SizedBox(height: 16),
          FilledButton(onPressed: retry, child: const Text('Retry')),
        ],
      ),
    ),
  );
}

Widget pageList(List<Widget> children) => ListView(
  padding: const EdgeInsets.all(16),
  physics: const AlwaysScrollableScrollPhysics(),
  children: children,
);
Widget title(String value) => Padding(
  padding: const EdgeInsets.symmetric(vertical: 12),
  child: Text(
    value,
    style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
  ),
);
Widget field(String name, dynamic value) => Padding(
  padding: const EdgeInsets.symmetric(vertical: 7),
  child: Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        name,
        style: const TextStyle(color: Colors.cyanAccent, fontSize: 12),
      ),
      SelectableText(text(value)),
    ],
  ),
);
Widget notice(String value) => Card(
  child: Padding(padding: const EdgeInsets.all(14), child: Text(value)),
);
void open(BuildContext context, String name, Widget child) =>
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => Scaffold(
          appBar: AppBar(title: Text(name)),
          body: child,
        ),
      ),
    );

class JsonEvidence extends StatelessWidget {
  final dynamic value;
  final String heading;
  const JsonEvidence({
    super.key,
    required this.value,
    this.heading = 'Evidence',
  });
  @override
  Widget build(BuildContext context) {
    final map = object(value);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        title(heading),
        if (map.isEmpty) notice('No evidence returned.'),
        ...map.entries.map((e) {
          if (e.value is List) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                title(e.key),
                ...(e.value as List).map(
                  (v) => v is Map
                      ? JsonEvidence(value: v, heading: 'Record')
                      : field(e.key, v),
                ),
              ],
            );
          }
          if (e.value is Map) {
            return JsonEvidence(value: e.value, heading: e.key);
          }
          return field(
            e.key,
            e.key.toLowerCase().endsWith('at') ? stamp(e.value) : e.value,
          );
        }),
      ],
    );
  }
}
