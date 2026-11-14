import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/api.dart';

class LoginScreen extends StatefulWidget {
  final ApiClient api;
  const LoginScreen({super.key, required this.api});
  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final form = GlobalKey<FormState>();
  final email = TextEditingController(),
      password = TextEditingController(),
      otp = TextEditingController();
  bool busy = false, activationRequired = false;
  String? error;
  Future<void> submit() async {
    if (busy || !form.currentState!.validate()) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final done = await widget.api.login(
        email.text,
        password.text,
        otp: activationRequired ? otp.text : null,
      );
      if (mounted && !done) setState(() => activationRequired = true);
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    email.dispose();
    password.dispose();
    otp.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 460),
            child: Form(
              key: form,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.flight_takeoff,
                    size: 60,
                    color: Colors.cyanAccent,
                  ),
                  const SizedBox(height: 20),
                  const Text(
                    'DroneOps',
                    style: TextStyle(fontSize: 34, fontWeight: FontWeight.bold),
                  ),
                  const Text(
                    'Operator access · mission visibility on the move',
                  ),
                  const SizedBox(height: 24),
                  TextFormField(
                    controller: email,
                    decoration: const InputDecoration(labelText: 'Email'),
                    keyboardType: TextInputType.emailAddress,
                    autofillHints: const [AutofillHints.username],
                    enabled: !busy,
                    validator: (v) => v != null && v.contains('@')
                        ? null
                        : 'Enter your email.',
                  ),
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: password,
                    decoration: const InputDecoration(labelText: 'Password'),
                    obscureText: true,
                    autofillHints: const [AutofillHints.password],
                    enabled: !busy,
                    validator: (v) =>
                        v == null || v.isEmpty ? 'Enter your password.' : null,
                  ),
                  if (activationRequired) ...[
                    const SizedBox(height: 16),
                    const Text(
                      'Enter the six-digit OTP from your approval email. Your password is kept only in this form until activation completes.',
                    ),
                    TextFormField(
                      controller: otp,
                      decoration: const InputDecoration(
                        labelText: 'Approval OTP',
                      ),
                      keyboardType: TextInputType.number,
                      autofillHints: const [AutofillHints.oneTimeCode],
                      inputFormatters: [
                        FilteringTextInputFormatter.digitsOnly,
                        LengthLimitingTextInputFormatter(6),
                      ],
                      validator: (v) => RegExp(r'^\d{6}$').hasMatch(v ?? '')
                          ? null
                          : 'Enter six digits.',
                    ),
                    TextButton(
                      onPressed: busy
                          ? null
                          : () async {
                              setState(() => busy = true);
                              try {
                                await widget.api.request(
                                  'POST',
                                  'pilot-registrations/first-time-login/resend-otp',
                                  anonymous: true,
                                  body: {
                                    'email': email.text.trim(),
                                    'password': password.text,
                                  },
                                );
                                if (mounted) {
                                  setState(
                                    () => error = 'OTP sent. Wait 60 seconds before resending.',
                                  );
                                }
                              } catch (e) {
                                if (mounted) {
                                  setState(() => error = e.toString());
                                }
                              } finally {
                                if (mounted) setState(() => busy = false);
                              }
                            },
                      child: const Text('Resend approval OTP'),
                    ),
                  ],
                  if (error != null)
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 16),
                      child: Semantics(
                        liveRegion: true,
                        child: Text(
                          error!,
                          style: const TextStyle(color: Colors.orangeAccent),
                        ),
                      ),
                    ),
                  const SizedBox(height: 20),
                  FilledButton(
                    onPressed: busy ? null : submit,
                    child: Text(
                      busy
                          ? 'Signing in…'
                          : activationRequired
                          ? 'Verify & sign in'
                          : 'Sign in',
                    ),
                  ),
                  const SizedBox(height: 20),
                  const Text(
                    'Route authoring and Manager administration are available in the Web portal. Use your approved Operator account.',
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
