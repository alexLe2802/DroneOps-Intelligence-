import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:droneops_mobile/core/polling.dart';

void main() {
  test(
    'foreground polling never overlaps and discards a paused response',
    () async {
      int reads = 0;
      final results = <int>[];
      final pending = Completer<int>();
      final poll = ForegroundPoller<int>(
        read: () {
          reads++;
          return pending.future;
        },
        onData: results.add,
        onError: (e) => fail('$e'),
        interval: const Duration(hours: 1),
      );
      poll.start();
      await poll.refresh();
      expect(reads, 1);
      poll.pause();
      pending.complete(5);
      await Future<void>.delayed(Duration.zero);
      expect(results, isEmpty);
      poll.dispose();
    },
  );
  test(
    'disposed requests cannot write late values; failures reach UI',
    () async {
      final pending = Completer<int>();
      final results = <int>[];
      final poll = ForegroundPoller<int>(
        read: () => pending.future,
        onData: results.add,
        onError: (e) => fail('$e'),
      );
      poll.start();
      poll.dispose();
      pending.complete(1);
      await Future<void>.delayed(Duration.zero);
      expect(results, isEmpty);
      Object? failure;
      final broken = ForegroundPoller<int>(
        read: () async => throw StateError('offline'),
        onData: results.add,
        onError: (e) => failure = e,
        interval: const Duration(hours: 1),
      );
      broken.start();
      await Future<void>.delayed(Duration.zero);
      expect(failure, isA<StateError>());
      broken.dispose();
    },
  );
}
