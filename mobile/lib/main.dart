import 'package:flutter/material.dart';

import 'core/api.dart';
import 'screens/login.dart';
import 'screens/missions.dart';
import 'screens/live.dart';
import 'screens/alerts.dart';
import 'screens/history.dart';
import 'screens/profile.dart';
import 'screens/shared.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  const base = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'https://api.droneops.click',
  );
  runApp(
    DroneOpsApp(
      api: base.isEmpty
          ? null
          : ApiClient(base: Uri.parse(base), store: SecureTokenStore()),
    ),
  );
}

class DroneOpsApp extends StatefulWidget {
  final ApiClient? api;
  const DroneOpsApp({super.key, required this.api});
  @override
  State<DroneOpsApp> createState() => _DroneOpsAppState();
}

class _DroneOpsAppState extends State<DroneOpsApp> {
  bool ready = false;
  Object? restoreError;
  int sessionGeneration = 0;
  @override
  void initState() {
    super.initState();
    widget.api?.addListener(changed);
    restore();
  }

  void changed() {
    if (mounted) setState(() => sessionGeneration++);
  }

  Future<void> restore() async {
    try {
      await widget.api?.restore();
      if (mounted) setState(() => restoreError = null);
    } catch (e) {
      if (mounted) setState(() => restoreError = e);
    } finally {
      if (mounted) setState(() => ready = true);
    }
  }

  @override
  void dispose() {
    widget.api?.removeListener(changed);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => MaterialApp(
    key: ValueKey(sessionGeneration),
    title: 'DroneOps Operator',
    debugShowCheckedModeBanner: false,
    theme: ThemeData(
      brightness: Brightness.dark,
      colorScheme: ColorScheme.fromSeed(
        seedColor: Colors.cyan,
        brightness: Brightness.dark,
      ),
      scaffoldBackgroundColor: const Color(0xff0b1420),
      inputDecorationTheme: const InputDecorationTheme(
        border: OutlineInputBorder(),
      ),
      useMaterial3: true,
    ),
    home: widget.api == null
        ? const Scaffold(
            body: SafeArea(
              child: Center(
                child: Padding(
                  padding: EdgeInsets.all(24),
                  child: Text(
                    'Backend not configured. Build with --dart-define=API_BASE_URL=https://your-backend-origin (without /api). No demo data is used.',
                  ),
                ),
              ),
            ),
          )
        : !ready
        ? const Scaffold(body: Center(child: CircularProgressIndicator()))
        : restoreError != null
        ? Scaffold(
            body: ErrorPanel(error: restoreError!, retry: restore),
          )
        : widget.api!.authenticated
        ? OperatorHome(key: ValueKey(sessionGeneration), api: widget.api!)
        : LoginScreen(api: widget.api!),
  );
}

class OperatorHome extends StatefulWidget {
  final ApiClient api;
  const OperatorHome({super.key, required this.api});
  @override
  State<OperatorHome> createState() => _OperatorHomeState();
}

class _OperatorHomeState extends State<OperatorHome> {
  int index = 0;
  @override
  Widget build(BuildContext context) {
    final pages = [
      MissionsScreen(api: widget.api),
      LiveScreen(api: widget.api),
      AlertsScreen(api: widget.api),
      HistoryScreen(api: widget.api),
      ProfileScreen(api: widget.api),
    ];
    return Scaffold(
      appBar: AppBar(
        title: Text(
          [
            'My missions',
            'Live monitoring',
            'Alerts & incidents',
            'Flight history',
            'My profile',
          ][index],
        ),
        actions: [
          IconButton(
            tooltip: 'Sign out',
            onPressed: widget.api.logout,
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: pages[index],
      bottomNavigationBar: NavigationBar(
        selectedIndex: index,
        onDestinationSelected: (v) => setState(() => index = v),
        destinations: const [
          NavigationDestination(icon: Icon(Icons.route), label: 'Missions'),
          NavigationDestination(icon: Icon(Icons.radar), label: 'Live'),
          NavigationDestination(
            icon: Icon(Icons.notifications),
            label: 'Alerts',
          ),
          NavigationDestination(icon: Icon(Icons.history), label: 'History'),
          NavigationDestination(icon: Icon(Icons.person), label: 'Profile'),
        ],
      ),
    );
  }
}
