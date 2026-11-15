import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:droneops_mobile/main.dart';
import 'package:droneops_mobile/core/api.dart';

import 'api_test.dart' show MemoryStore;

void main() {
  testWidgets(
    'unconfigured builds explain missing backend without fake missions',
    (tester) async {
      await tester.pumpWidget(const DroneOpsApp(api: null));
      await tester.pumpAndSettle();
      expect(find.textContaining('Backend not configured'), findsOneWidget);
      expect(find.text('Missions'), findsNothing);
    },
  );
  testWidgets(
    'Operator signs in, views missions and logs out without authoring controls',
    (tester) async {
      final api = ApiClient(
        base: Uri.parse('https://backend.test'),
        store: MemoryStore(),
        client: MockClient((req) async {
          if (req.url.path == '/api/auth/login') {
            return http.Response('{"accessToken":"fixture"}', 200);
          }
          if (req.url.path == '/api/users/profile') {
            return http.Response(
              '{"role":"uav_operator","fullName":"Pilot"}',
              200,
            );
          }
          return http.Response(
            jsonEncode([
              {
                'id': '00000000-0000-0000-0000-000000000001',
                'name': 'Inspection',
                'status': 'Approved',
                'uavCode': 'UAV-01',
              },
            ]),
            200,
          );
        }),
      );
      await tester.pumpWidget(DroneOpsApp(api: api));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Email'),
        'pilot@example.test',
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Password'),
        'fixture',
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.pumpAndSettle();
      expect(find.text('Inspection'), findsOneWidget);
      expect(find.text('New mission'), findsNothing);
      expect(find.text('Arm'), findsNothing);
      await tester.tap(find.byTooltip('Sign out'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(FilledButton, 'Sign in'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      api.dispose();
    },
  );
}
