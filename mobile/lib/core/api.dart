import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

typedef Json = Map<String, dynamic>;
Json object(dynamic value) =>
    value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};
List<Json> records(dynamic value) {
  final list = value is List ? value : object(value)['items'];
  if (list is! List) {
    throw const ApiException(
      'The server returned an invalid list. Try refreshing.',
      502,
    );
  }
  return list.map(object).toList();
}

class ApiException implements Exception {
  final String message;
  final int status;
  const ApiException(this.message, this.status);
  @override
  String toString() => message;
}

abstract interface class TokenStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<void> clear();
}

class SecureTokenStore implements TokenStore {
  final FlutterSecureStorage storage;
  SecureTokenStore([this.storage = const FlutterSecureStorage()]);
  @override
  Future<String?> read() => storage.read(key: 'droneops_access');
  @override
  Future<void> write(String value) =>
      storage.write(key: 'droneops_access', value: value);
  @override
  Future<void> clear() => storage.delete(key: 'droneops_access');
}

class ApiClient extends ChangeNotifier {
  final Uri base;
  final http.Client client;
  final TokenStore store;
  String? _token;
  Json? profile;
  bool get authenticated => _token != null && profile != null;
  ApiClient({required this.base, required this.store, http.Client? client})
    : client = client ?? http.Client() {
    if (base.scheme != 'https' &&
        !(kDebugMode &&
            ['localhost', '127.0.0.1', '10.0.2.2'].contains(base.host))) {
      throw ArgumentError(
        'Use an HTTPS backend origin. HTTP is limited to loopback debug builds.',
      );
    }
    if (base.userInfo.isNotEmpty ||
        base.query.isNotEmpty ||
        base.fragment.isNotEmpty ||
        (base.path.isNotEmpty && base.path != '/')) {
      throw ArgumentError(
        'Use the backend origin without /api, credentials or query.',
      );
    }
  }
  Uri uri(String path, [Map<String, String>? query]) => base.replace(
    path: '/api/$path',
    queryParameters: query?.isEmpty == true ? null : query,
  );
  Future<dynamic> request(
    String method,
    String path, {
    Json? body,
    Map<String, String>? query,
    bool anonymous = false,
  }) async {
    final headers = <String, String>{
      'accept': 'application/json',
      if (body != null) 'content-type': 'application/json',
      if (!anonymous && _token != null) 'authorization': 'Bearer $_token',
    };
    try {
      final req = http.Request(method, uri(path, query))
        ..headers.addAll(headers);
      if (body != null) req.body = jsonEncode(body);
      final res = await http.Response.fromStream(
        await client.send(req).timeout(const Duration(seconds: 60)),
      ).timeout(const Duration(seconds: 30));
      dynamic data;
      try {
        data = res.body.isEmpty ? null : jsonDecode(res.body);
      } catch (_) {
        if (res.statusCode < 400) {
          throw const ApiException('Invalid server response.', 502);
        }
      }
      if (res.statusCode == 401 && !anonymous) {
        await logout();
        throw const ApiException('Session expired. Sign in again.', 401);
      }
      if (res.statusCode >= 400) {
        final d = object(data);
        final errors = object(d['errors']).values
            .expand((v) => v is List ? v : [v])
            .whereType<String>();
        throw ApiException(
          d['message']?.toString() ??
              d['Message']?.toString() ??
              errors.firstOrNull ??
              (res.statusCode == 403
                  ? 'You do not have permission to view this record.'
                  : res.statusCode == 409
                  ? 'This record changed. Reload before saving.'
                  : res.statusCode == 429
                  ? 'Too many attempts. Wait before trying again.'
                  : 'Request failed (${res.statusCode}). Retry shortly.'),
          res.statusCode,
        );
      }
      return data;
    } on TimeoutException {
      throw const ApiException(
        'Backend is taking too long. It may be starting up. Retry shortly.',
        504,
      );
    } on http.ClientException {
      throw const ApiException(
        'Cannot reach the backend. Check your connection and retry.',
        0,
      );
    }
  }

  Future<bool> login(String email, String password, {String? otp}) async {
    final result = object(
      await request(
        'POST',
        otp == null ? 'auth/login' : 'pilot-registrations/first-time-login',
        anonymous: true,
        body: {'email': email.trim(), 'password': password, 'otp': ?otp},
      ),
    );
    if (result['requiresFirstLogin'] == true) return false;
    final token = result['accessToken'];
    if (token is! String || token.isEmpty) {
      throw const ApiException('Login returned no session.', 502);
    }
    _token = token;
    try {
      await loadProfile();
      await store.write(token);
    } catch (_) {
      await logout();
      rethrow;
    }
    notifyListeners();
    return true;
  }

  Future<void> restore() async {
    _token = await store.read();
    if (_token == null) return;
    try {
      await loadProfile();
    } on ApiException catch (e) {
      if (e.status == 401 || e.status == 403) await logout();
      rethrow;
    }
  }

  Future<Json> loadProfile() async {
    final p = object(await request('GET', 'users/profile'));
    if (p['role'] != 'uav_operator' && p['role'] != 'Pilot') {
      await logout();
      throw const ApiException(
        'This app is for UAV Operators. Use the Web portal for Manager access.',
        403,
      );
    }
    profile = p;
    return p;
  }

  // The JWT backend has no logout endpoint/revocation contract. Destroy local credentials;
  // every subsequent data request still requires a backend-validated token.
  Future<void> logout() async {
    _token = null;
    profile = null;
    await store.clear();
    notifyListeners();
  }

  Future<Json> mission(String id) async =>
      object(await request('GET', 'missions/${identity(id)}'));
  Future<List<Json>> missions() async =>
      records(await request('GET', 'missions/my'));
  static String identity(String id) {
    if (!RegExp(r'^[0-9a-fA-F-]{36}$').hasMatch(id)) {
      throw const ApiException('Invalid record identity.', 400);
    }
    return id;
  }

  @override
  void dispose() {
    client.close();
    super.dispose();
  }
}
