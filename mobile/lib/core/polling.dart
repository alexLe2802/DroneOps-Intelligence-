import 'dart:async';

// One in-flight read at a time; generations prevent an older read from updating a new scope.
class ForegroundPoller<T> {
  final Future<T> Function() read;
  final void Function(T) onData;
  final void Function(Object) onError;
  final Duration interval;
  Timer? _timer;
  bool _enabled = false, _busy = false, _disposed = false;
  int _generation = 0;
  ForegroundPoller({
    required this.read,
    required this.onData,
    required this.onError,
    this.interval = const Duration(seconds: 5),
  });
  void start() {
    if (_disposed || _enabled) return;
    _enabled = true;
    unawaited(refresh());
  }

  void pause() {
    _enabled = false;
    _generation++;
    _timer?.cancel();
  }

  Future<void> refresh() async {
    if (_busy || !_enabled || _disposed) return;
    _timer?.cancel();
    _busy = true;
    final generation = _generation;
    try {
      final data = await read();
      if (_enabled && !_disposed && generation == _generation) onData(data);
    } catch (e) {
      if (_enabled && !_disposed && generation == _generation) onError(e);
    } finally {
      _busy = false;
      if (_enabled && !_disposed) _timer = Timer(interval, refresh);
    }
  }

  void dispose() {
    _disposed = true;
    pause();
  }
}
