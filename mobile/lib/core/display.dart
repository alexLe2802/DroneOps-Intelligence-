import 'api.dart';

String text(dynamic value) => value == null || value.toString().trim().isEmpty
    ? 'Unavailable'
    : value.toString();
String stamp(dynamic value) {
  final parsed = DateTime.tryParse(value?.toString() ?? '');
  if (parsed == null) return 'Not recorded';
  final d = parsed.toUtc().add(const Duration(hours: 7));
  String two(int n) => n.toString().padLeft(2, '0');
  return '${two(d.day)}/${two(d.month)}/${d.year} ${two(d.hour)}:${two(d.minute)}:${two(d.second)} GMT+7';
}

bool fresh(Json data, DateTime now) {
  if (data['isValid'] != true ||
      data['isStale'] == true ||
      data['connectivityStatus'] != 'Online') {
    return false;
  }
  final maxAge = (data['staleAfterSeconds'] as num?)?.toDouble() ?? 10;
  for (final key in ['receivedAt', 'sourceTimestamp']) {
    final t = DateTime.tryParse(data[key]?.toString() ?? '');
    if (t == null) return false;
    final seconds = now.toUtc().difference(t.toUtc()).inMilliseconds / 1000;
    if (seconds < -5 || seconds > maxAge) return false;
  }
  return true;
}

String measurement(dynamic value, String unit, {bool valid = true}) =>
    valid && value is num && value.isFinite
    ? '${value.toStringAsFixed(1)} $unit'
    : 'Unavailable';
