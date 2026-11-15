import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:droneops_mobile/core/api.dart';
import 'package:droneops_mobile/screens/profile.dart';
import 'package:droneops_mobile/screens/alerts.dart';

import 'api_test.dart' show MemoryStore;

void main() {
  testWidgets(
    'notification mark-read uses PATCH contract and only changes after success',
    (tester) async {
      final api = ApiClient(
        base: Uri.parse('https://backend.test'),
        store: MemoryStore(),
        client: MockClient((r) async {
          expect(r.method, 'PATCH');
          expect(
            r.url.path,
            '/api/notifications/00000000-0000-0000-0000-000000000001/read',
          );
          return http.Response('{"isRead":true}', 200);
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: NotificationScreen(
              api: api,
              item: {
                'id': '00000000-0000-0000-0000-000000000001',
                'title': 'Battery alert',
                'message': 'Recorded incident',
                'isRead': false,
              },
            ),
          ),
        ),
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Mark as read'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(FilledButton, 'Read'), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      api.dispose();
    },
  );
  testWidgets(
    'profile name saves independently of missing DOB and stops on stale conflict',
    (tester) async {
      int writes = 0;
      final api = ApiClient(
        base: Uri.parse('https://backend.test'),
        store: MemoryStore(),
        client: MockClient((r) async {
          if (r.method == 'PUT') {
            writes++;
            expect(jsonDecode(r.body), {
              'fullName': 'Changed pilot',
              'expectedVersion': 4,
            });
            return http.Response('{"message":"Reload latest profile"}', 409);
          }
          if (r.url.path.endsWith('pilot-profile')) {
            return http.Response(
              '{"profileVersion":4,"dateOfBirth":null,"experienceYears":1}',
              200,
            );
          }
          return http.Response(
            '{"role":"uav_operator","fullName":"Pilot","profileVersion":4}',
            200,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: ProfileScreen(api: api)),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Full name'),
        'Changed pilot',
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Save name'));
      await tester.pumpAndSettle();
      expect(writes, 1);
      expect(find.textContaining('Profile changed elsewhere'), findsOneWidget);
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Save name'),
            )
            .onPressed,
        isNull,
      );
      await tester.pumpWidget(const SizedBox());
      api.dispose();
    },
  );
}
