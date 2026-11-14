import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:droneops_mobile/core/api.dart';
import 'package:droneops_mobile/core/display.dart';

class MemoryStore implements TokenStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String v) async {
    value = v;
  }

  @override
  Future<void> clear() async {
    value = null;
  }
}

void main() {
  test('Operator login stores only token and sends bearer on scoped reads; logout clears it', () async {
    final store = MemoryStore();
    final seen = <http.Request>[];
    final api = ApiClient(
      base: Uri.parse('https://backend.test'),
      store: store,
      client: MockClient((req) async {
        seen.add(req);
        if (req.url.path == '/api/auth/login') {
          expect(jsonDecode(req.body), {
            'email': 'pilot@example.test',
            'password': 'fixture',
          });
          return http.Response(
            jsonEncode({'accessToken': 'fixture-token'}),
            200,
          );
        }
        expect(req.headers['authorization'], 'Bearer fixture-token');
        if (req.url.path == '/api/users/profile') {
          return http.Response(
            jsonEncode({'id': 'pilot', 'role': 'uav_operator'}),
            200,
          );
        }
        return http.Response('[]', 200);
      }),
    );
    expect(await api.login(' pilot@example.test ', 'fixture'), true);
    expect(store.value, 'fixture-token');
    await api.missions();
    expect(seen.last.url.path, '/api/missions/my');
    await api.logout();
    expect(store.value, isNull);
    expect(api.authenticated, false);
    api.dispose();
  });
  test('Manager cannot retain an Operator app session', () async {
    final store = MemoryStore();
    final api = ApiClient(
      base: Uri.parse('https://backend.test'),
      store: store,
      client: MockClient(
        (req) async => http.Response(
          req.url.path.endsWith('login')
              ? ' {"accessToken":"fixture"}'
              : '{"role":"operation_manager"}',
          200,
        ),
      ),
    );
    await expectLater(
      api.login('manager@example.test', 'fixture'),
      throwsA(isA<ApiException>().having((e) => e.status, 'status', 403)),
    );
    expect(store.value, isNull);
    expect(api.authenticated, false);
    api.dispose();
  });
  test('first login requires OTP and does not persist a password or premature token', () async {
    final store = MemoryStore();
    int count = 0;
    final api = ApiClient(
      base: Uri.parse('https://backend.test'),
      store: store,
      client: MockClient((req) async {
        count++;
        if (count == 1) {
          return http.Response('{"requiresFirstLogin":true}', 200);
        }
        if (count == 2) {
          expect(req.url.path, '/api/pilot-registrations/first-time-login');
          expect(jsonDecode(req.body)['otp'], '012345');
          return http.Response('{"accessToken":"fixture"}', 200);
        }
        return http.Response('{"role":"uav_operator"}', 200);
      }),
    );
    expect(await api.login('pilot@example.test', 'fixture'), false);
    expect(store.value, isNull);
    expect(
      await api.login('pilot@example.test', 'fixture', otp: '012345'),
      true,
    );
    expect(store.value, 'fixture');
    api.dispose();
  });
  test(
    'restore validates token with backend; 401 clears local session',
    () async {
      final store = MemoryStore()..value = 'expired';
      final api = ApiClient(
        base: Uri.parse('https://backend.test'),
        store: store,
        client: MockClient(
          (_) async => http.Response('{"message":"Expired"}', 401),
        ),
      );
      await expectLater(api.restore(), throwsA(isA<ApiException>()));
      expect(store.value, isNull);
      expect(api.authenticated, false);
      api.dispose();
    },
  );
  test('profile mutation preserves expectedVersion and HTTP409 without silent overwrite', () async {
    final api = ApiClient(
      base: Uri.parse('https://backend.test'),
      store: MemoryStore(),
      client: MockClient((req) async {
        expect(req.method, 'PUT');
        expect(jsonDecode(req.body), {
          'fullName': 'Changed',
          'expectedVersion': 4,
        });
        return http.Response('{"message":"Reload latest profile"}', 409);
      }),
    );
    await expectLater(
      api.request(
        'PUT',
        'users/profile',
        body: {'fullName': 'Changed', 'expectedVersion': 4},
      ),
      throwsA(isA<ApiException>().having((e) => e.status, 'status', 409)),
    );
    api.dispose();
  });
  test('history query serializes exact scoped filters and nullable payload remains missing', () async {
    final api = ApiClient(
      base: Uri.parse('https://backend.test'),
      store: MemoryStore(),
      client: MockClient((req) async {
        expect(req.url.queryParameters, {
          'uavId': 'aircraft',
          'fromDate': '2026-11-01T00:00:00Z',
          'pageNumber': '2',
        });
        return http.Response('{"items":[{"batteryPercent":null}]}', 200);
      }),
    );
    final list = records(
      await api.request(
        'GET',
        'flight-history',
        query: {
          'uavId': 'aircraft',
          'fromDate': '2026-11-01T00:00:00Z',
          'pageNumber': '2',
        },
      ),
    );
    expect(list.single['batteryPercent'], isNull);
    expect(measurement(null, '%'), 'Unavailable');
    expect(measurement(0, '%'), '0.0 %');
    api.dispose();
  });
  test(
    'freshness rejects old/future/missing source data despite Online status',
    () {
      final now = DateTime.utc(2026, 11, 14, 10);
      final data = {
        'isValid': true,
        'connectivityStatus': 'Online',
        'isStale': false,
        'receivedAt': now.toIso8601String(),
        'sourceTimestamp': now.toIso8601String(),
        'staleAfterSeconds': 10,
      };
      expect(fresh(data, now), true);
      expect(fresh({...data, 'sourceTimestamp': null}, now), false);
      expect(fresh(data, now.add(const Duration(seconds: 11))), false);
      expect(
        fresh({
          ...data,
          'sourceTimestamp': now
              .add(const Duration(minutes: 1))
              .toIso8601String(),
        }, now),
        false,
      );
    },
  );
  test(
    'invalid identities and credential-bearing origins fail before network',
    () {
      expect(
        () => ApiClient.identity('../../admin'),
        throwsA(isA<ApiException>()),
      );
      expect(
        () => ApiClient(
          base: Uri.parse('https://user:pass@backend.test'),
          store: MemoryStore(),
        ),
        throwsArgumentError,
      );
      expect(
        () => ApiClient(
          base: Uri.parse('https://backend.test/api'),
          store: MemoryStore(),
        ),
        throwsArgumentError,
      );
    },
  );
}
