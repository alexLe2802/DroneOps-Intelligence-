import 'package:flutter/material.dart';

import '../core/api.dart';
import 'shared.dart';

class ProfileScreen extends StatefulWidget {
  final ApiClient api;
  const ProfileScreen({super.key, required this.api});
  @override
  State<ProfileScreen> createState() => _ProfileScreenState();
}

class _ProfileScreenState extends State<ProfileScreen> {
  final name = TextEditingController(),
      phone = TextEditingController(),
      years = TextEditingController(),
      birth = TextEditingController();
  final form = GlobalKey<FormState>();
  Json? profile, pilot;
  String? error, message;
  bool busy = false, stale = false;
  bool validatingOperator = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    try {
      final p = await widget.api.loadProfile();
      final o = object(await widget.api.request('GET', 'users/pilot-profile'));
      if (!mounted) return;
      setState(() {
        profile = p;
        pilot = o;
        name.text = p['fullName'] ?? '';
        phone.text = o['phoneNumber'] ?? '';
        years.text = '${o['experienceYears'] ?? 0}';
        birth.text = (o['dateOfBirth'] ?? '').toString().split('T').first;
        error = null;
        stale = false;
      });
    } catch (e) {
      if (mounted) setState(() => error = e.toString());
    }
  }

  Future<void> save(bool operator) async {
    if (busy || stale) return;
    validatingOperator = operator;
    if (!form.currentState!.validate()) return;
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      await widget.api.request(
        'PUT',
        operator ? 'users/pilot-profile' : 'users/profile',
        body: operator
            ? {
                'expectedVersion': pilot!['profileVersion'],
                'phoneNumber': phone.text.trim(),
                'dateOfBirth': birth.text,
                'experienceYears': int.parse(years.text),
              }
            : {
                'expectedVersion': profile!['profileVersion'],
                'fullName': name.text.trim(),
              },
      );
      await load();
      if (mounted) setState(() => message = 'Profile saved.');
    } catch (e) {
      if (mounted) {
        setState(() {
          error = e.toString();
          if (e is ApiException && e.status == 409) stale = true;
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  void dispose() {
    name.dispose();
    phone.dispose();
    years.dispose();
    birth.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (profile == null) {
      if (error != null) return ErrorPanel(error: error!, retry: load);
      return const Center(child: CircularProgressIndicator());
    }
    return Form(
      key: form,
      child: pageList([
        title('My profile'),
        field('Email', profile!['email']),
        field('Role', profile!['role']),
        if (error != null) Semantics(liveRegion: true, child: notice(error!)),
        if (message != null) notice(message!),
        if (stale) ...[
          notice(
            'Profile changed elsewhere. Reload the latest version before saving.',
          ),
          FilledButton(
            onPressed: busy ? null : load,
            child: const Text('Reload latest profile'),
          ),
        ],
        TextFormField(
          controller: name,
          enabled: !busy && !stale,
          decoration: const InputDecoration(labelText: 'Full name'),
          maxLength: 100,
          validator: (v) =>
              !validatingOperator && (v == null || v.trim().length < 2)
              ? 'Enter at least two characters.'
              : null,
        ),
        FilledButton(
          onPressed: busy || stale ? null : () => save(false),
          child: const Text('Save name'),
        ),
        const SizedBox(height: 24),
        TextFormField(
          controller: phone,
          enabled: !busy && !stale,
          keyboardType: TextInputType.phone,
          maxLength: 20,
          decoration: const InputDecoration(labelText: 'Phone number'),
        ),
        TextFormField(
          controller: birth,
          enabled: !busy && !stale,
          readOnly: true,
          decoration: const InputDecoration(labelText: 'Date of birth'),
          validator: (v) =>
              validatingOperator && DateTime.tryParse(v ?? '') == null
              ? 'Choose date of birth.'
              : null,
          onTap: () async {
            final now = DateTime.now();
            final date = await showDatePicker(
              context: context,
              initialDate:
                  DateTime.tryParse(birth.text) ??
                  DateTime(now.year - 18, now.month, now.day),
              firstDate: DateTime(1900),
              lastDate: DateTime(now.year - 18, now.month, now.day),
            );
            if (date != null) {
              birth.text =
                  '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
            }
          },
        ),
        TextFormField(
          controller: years,
          enabled: !busy && !stale,
          keyboardType: TextInputType.number,
          decoration: const InputDecoration(
            labelText: 'Flight experience (years)',
          ),
          validator: (v) =>
              validatingOperator &&
                  (int.tryParse(v ?? '') == null || int.parse(v!) < 0)
              ? 'Enter a non-negative whole number.'
              : null,
        ),
        FilledButton(
          onPressed: busy || stale ? null : () => save(true),
          child: const Text('Save Operator details'),
        ),
        title('Approved license'),
        field('License number', pilot!['pilotLicenseNo']),
        field('Issued', pilot!['licenseIssuedDate']),
        field('Expires', pilot!['licenseExpiredDate']),
        notice(
          'Approved credentials are read-only. License proposals and supporting evidence remain in the Web profile workflow.',
        ),
        const SizedBox(height: 24),
        OutlinedButton(
          onPressed: busy ? null : widget.api.logout,
          child: const Text('Sign out of this device'),
        ),
      ]),
    );
  }
}
