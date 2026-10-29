from copy import deepcopy
from pathlib import Path
from shutil import copy2
from zipfile import ZIP_DEFLATED, ZipFile
import tempfile
import xml.etree.ElementTree as ET

SOURCE = Path('/Users/alexxxx/Downloads/Report3_Software Requirement Specification.docx')
OUTPUT = Path('Report3_Software_Requirement_Specification_revised.docx')
W_NS = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
W = f'{{{W_NS}}}'
ET.register_namespace('w', W_NS)


SCREENS = [
    ('SCR-01', 'Shared', 'Landing Page', '/', 'Public entry point; navigates to Sign In.'),
    ('SCR-02', 'FE-01', 'Sign In', '/login', 'Authenticates a user; opens Pilot Registration on request and First-time Activation on first login.'),
    ('SCR-03', 'FE-01', 'Pilot Registration', 'Modal on /login', 'Collects a pilot application and verification data before account review.'),
    ('SCR-04', 'FE-01', 'First-time Activation', '/login/first-time', 'Completes first-login activation, then opens the Operational Dashboard.'),
    ('SCR-05', 'FE-10', 'Operational Dashboard', '/portal', 'Authenticated landing screen; resolves the session role and exposes role-scoped navigation.'),
    ('SCR-06', 'Shared', 'Profile & Account', '/portal/profile', 'Allows either role to view and maintain its own account and profile.'),
    ('SCR-07', 'Shared', 'Notification Center', 'Contextual deep links', 'Opened from the dashboard bell; links the user to an authorized related record.'),
    ('SCR-08', 'FE-01', 'Operators', '/portal/operators', 'Manager screen for operator review, status and assignments.'),
    ('SCR-09', 'FE-01', 'UAV Management', '/portal/uav-management', 'Manager screen for fleet-wide UAV records and status.'),
    ('SCR-10', 'FE-01/04', 'Fleet & Restrictions', '/portal/geofences', 'Manager screen for fleet context, geofences and operational restrictions.'),
    ('SCR-11', 'FE-02', 'Mission Planning', '/portal/mission-planning', 'Manager planning workspace; sends a mission to Validation & Approvals for review.'),
    ('SCR-12', 'FE-03/04', 'Validation & Approvals', '/portal/mission-approvals', 'Manager queue for validation evidence and approval decisions; opens Mission Details.'),
    ('SCR-13', 'FE-02/03/04', 'Mission Details', '/portal/missions/[id]', 'Manager view of mission content, validation and decision history.'),
    ('SCR-14', 'FE-05/06/07', 'Live Monitoring', '/portal/live-monitoring', 'Role-scoped live mission view; links alerts to incidents and completion to history.'),
    ('SCR-15', 'FE-07/08', 'Incident Management', '/portal/incidents', 'Manager incident list and operational management screen.'),
    ('SCR-16', 'FE-08', 'Incident Details', '/portal/incidents/[id]', 'Detailed incident evidence and permitted lifecycle actions.'),
    ('SCR-17', 'FE-08', 'Flight History', '/portal/flight-history', 'Manager list of completed flights; opens a Post-flight Report.'),
    ('SCR-18', 'FE-08', 'Post-flight Report', '/portal/flight-history/[missionId]', 'Manager report view with links to related incident details.'),
    ('SCR-19', 'FE-09', 'AI Risk Assessment', '/portal/ai-assessment', 'Role-scoped advisory risk assessment for an authorized mission.'),
    ('SCR-20', 'FE-01', 'Assigned UAV', '/portal/uav', 'Operator view of assigned UAVs; opens UAV create/detail/edit where permitted.'),
    ('SCR-21', 'FE-01', 'UAV Add / Detail / Edit', '/portal/uav/new · /portal/uav/[id] · /portal/uav/[id]/edit', 'Operator UAV workflow; pairs the selected UAV through Connect Drone.'),
    ('SCR-22', 'FE-05', 'Connect Drone', '/portal/drone-connection', 'Pairs/connects an authorized UAV and proceeds to Live Monitoring.'),
    ('SCR-23', 'FE-02', 'My Missions', '/portal/missions', 'Operator mission list; opens mission create/detail/edit.'),
    ('SCR-24', 'FE-02', 'New / Detail / Edit Mission', '/portal/missions/new · /portal/missions/[id] · /portal/missions/[id]/edit', 'Operator mission workflow; launches validation or AI assessment.'),
    ('SCR-25', 'FE-04', 'Validation Results', '/portal/validation-results', 'Shows the result and evidence produced by validating a mission.'),
    ('SCR-26', 'FE-07/08', 'My Incidents / Details', '/portal/incidents · /portal/incidents/[id]', 'Operator-scoped incident list and details reached from live alerts.'),
    ('SCR-27', 'FE-08', 'Flight History / Report', '/flight-history · /flight-history/[missionId]', 'Operator-scoped completed-flight history and report.'),
]

AUTH = [
    ('Landing Page; Sign In; Pilot Registration; First-time Activation', 'Public for entry/registration; activation requires a valid first-login challenge.', 'Same authentication entry; Manager privilege is never self-selected.'),
    ('Operational Dashboard; Profile & Account; Notification Center', 'Own account and own/assigned deep links only.', 'Own account and managed-scope deep links only.'),
    ('Operators', 'No administrative access; own profile is handled in Profile & Account.', 'Manage operator review, status and assignments within scope.'),
    ('UAV Management', 'No fleet-wide management access.', 'Manage UAV records and status within scope.'),
    ('Fleet & Restrictions', 'View relevant restrictions through authorized mission context; no edits.', 'Create and revise geofences/restrictions; changes trigger stale-validation policy.'),
    ('Mission Planning; Validation & Approvals; Manager Mission Details', 'No access to Manager-only screens.', 'Plan/review managed missions; approval requires fresh passing validation and separation of duties.'),
    ('Assigned UAV; UAV Add / Detail / Edit; Connect Drone', 'Access assigned UAVs and permitted own UAV workflow; connect only an authorized configured UAV.', 'No Operator-lane navigation; fleet administration uses UAV Management.'),
    ('My Missions; New / Detail / Edit Mission; Validation Results', 'Create and maintain own/assigned pre-flight missions and review their validation results.', 'Use Manager mission screens for managed records and decisions.'),
    ('Live Monitoring', 'View own/assigned active mission and follow alerts/completion links.', 'View active missions within managed scope.'),
    ('Incident Management / My Incidents; Incident Details', 'View and acknowledge own/assigned incidents; cannot resolve.', 'View, acknowledge and resolve managed incidents with required reason/note.'),
    ('Flight History; Post-flight Report', 'View own/assigned completed flights and reports.', 'View managed flights; retry/regenerate reports with audit history.'),
    ('AI Risk Assessment', 'Generate/retry/view for own/assigned missions; cannot overwrite provider output.', 'Generate/retry/view for managed missions; cannot overwrite provider output.'),
]


def text_of(element):
    return ''.join((node.text or '') for node in element.iter(W + 't')).strip()


def set_paragraph_text(paragraph, value):
    p_pr = paragraph.find(W + 'pPr')
    for child in list(paragraph):
        if child is not p_pr:
            paragraph.remove(child)
    run = ET.SubElement(paragraph, W + 'r')
    text = ET.SubElement(run, W + 't')
    if value.startswith(' ') or value.endswith(' '):
        text.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
    text.text = value


def set_cell_text(cell, value):
    paragraphs = cell.findall(W + 'p')
    if not paragraphs:
        paragraphs = [ET.SubElement(cell, W + 'p')]
    set_paragraph_text(paragraphs[0], value)
    for paragraph in paragraphs[1:]:
        cell.remove(paragraph)


def replace_table_rows(table, headers, rows):
    existing = table.findall(W + 'tr')
    template = existing[1] if len(existing) > 1 else existing[0]
    for row in existing:
        table.remove(row)
    for values in (headers, *rows):
        row = deepcopy(template)
        cells = row.findall(W + 'tc')
        while len(cells) < len(values):
            row.append(deepcopy(cells[-1]))
            cells = row.findall(W + 'tc')
        for cell, value in zip(cells, values):
            set_cell_text(cell, value)
        for cell in cells[len(values):]:
            row.remove(cell)
        table.append(row)


copy2(SOURCE, OUTPUT)
with ZipFile(OUTPUT, 'r') as archive:
    files = {name: archive.read(name) for name in archive.namelist()}

root = ET.fromstring(files['word/document.xml'])
body = root.find(W + 'body')
children = list(body)

# Normalize the subsection title and replace the overview sentence with wording
# that defines the diagram as the navigation source of truth.
for element in children:
    value = text_of(element)
    if value == '3.1.1 Screens Flow':
        set_paragraph_text(element, '3.1.1 Screen Flow')
    elif value.startswith('Feature IDs FE-01 to FE-10 preserve Report 1 traceability.'):
        set_paragraph_text(element, 'Feature IDs FE-01 to FE-10 preserve Report 1 traceability. UC IDs identify user/system interactions. The screen flow is the source of truth for user-visible navigation: shared entry and account screens lead to role-scoped Operations Manager or UAV Operator lanes. Each detailed function defines normal behavior, validation/exception behavior and its persisted or displayed outcome; background processing is specified separately.')

# Locate the first table after each target heading.
for heading, headers, rows in [
    ('3.1.2 Screen Descriptions', ('ID', 'Feature', 'Screen', 'Route', 'Description / Navigation'), SCREENS),
    ('3.1.3 Screen Authorization', ('Screen / Action', 'UAV Operator', 'Operations Manager'), AUTH),
]:
    start = next(i for i, e in enumerate(children) if text_of(e) == heading)
    table = next(e for e in children[start + 1:] if e.tag == W + 'tbl')
    replace_table_rows(table, headers, rows)

# Terminology and navigation labels used by the detailed functional requirements.
replacements = {
    'Management > UAVs': 'UAV Management (/portal/uav-management)',
    'Management > Operators': 'Operators (/portal/operators)',
    'Management > Operational Areas & Restrictions': 'Fleet & Restrictions (/portal/geofences)',
    'Areas & Restrictions': 'Fleet & Restrictions',
    'Mission History': 'Flight History',
    'Missions > New Mission or Details > Edit': 'My Missions > New / Detail / Edit Mission',
    'Approvals > pending mission': 'Validation & Approvals > pending mission',
    'Approvals > select pending mission': 'Validation & Approvals > select pending mission',
    'Mission Details > Validation': 'Validation Results',
    'Mission Details or Live Monitoring > AI Assessment': 'New / Detail / Edit Mission or Live Monitoring > AI Risk Assessment',
    'Dashboard > Analytics': 'Operational Dashboard > authorized history and summary links',
}
for element in root.iter(W + 't'):
    if not element.text:
        continue
    for old, new in replacements.items():
        element.text = element.text.replace(old, new)

# Keep each detailed use case traceable to the screen IDs introduced above.
use_case_screens = {
    'UC-01': 'SCR-02',
    'UC-02': 'SCR-09 / SCR-20 / SCR-21',
    'UC-03': 'SCR-08',
    'UC-04': 'SCR-10',
    'UC-23': 'SCR-03 / SCR-08',
    'UC-24': 'SCR-06',
    'UC-25': 'SCR-06',
    'UC-26': 'SCR-06 / SCR-08',
    'UC-05': 'SCR-23 / SCR-24',
    'UC-06': 'SCR-24 / SCR-13',
    'UC-07': 'SCR-23 / SCR-24 / SCR-13',
    'UC-08': 'SCR-24 / SCR-25',
    'UC-09': 'SCR-12 / SCR-13',
    'UC-10': 'SCR-13 / SCR-24',
    'UC-11': 'SCR-25 / background',
    'UC-12': 'SCR-25 / SCR-13',
    'UC-13': 'Non-screen',
    'UC-14': 'SCR-14',
    'UC-15': 'SCR-14 / SCR-15 / SCR-26 / background',
    'UC-16': 'SCR-15 / SCR-16 / SCR-26',
    'UC-17': 'SCR-18 / SCR-27 / background',
    'UC-18': 'SCR-17 / SCR-18 / SCR-27',
    'UC-19': 'SCR-19',
    'UC-20': 'SCR-19',
    'UC-21': 'SCR-05',
    'UC-22': 'SCR-05',
}
current_uc = None
for element in list(body):
    value = text_of(element)
    if element.tag == W + 'p' and '(UC-' in value:
        current_uc = next((uc for uc in use_case_screens if f'({uc})' in value), None)
        if current_uc == 'UC-22':
            for node in element.iter(W + 't'):
                if node.text:
                    node.text = node.text.replace('Review operational analytics', 'Review operational summaries')
    elif element.tag == W + 'tbl' and current_uc:
        for row in element.findall(W + 'tr'):
            cells = row.findall(W + 'tc')
            if len(cells) >= 2 and text_of(cells[0]) == 'Actors / Interface':
                actor_text = text_of(cells[1]).split(' | ')[0]
                set_cell_text(cells[1], f'{actor_text} | {use_case_screens[current_uc]}')
    elif current_uc and element.tag == W + 'p' and value.startswith('Screen layout — SCR-'):
        set_paragraph_text(element, f'Screen layout — {use_case_screens[current_uc].replace(" / background", "").replace("Non-screen", "background function")}')

files['word/document.xml'] = ET.tostring(root, encoding='utf-8', xml_declaration=True)
tmp = Path(tempfile.mkstemp(suffix='.docx')[1])
try:
    with ZipFile(tmp, 'w', ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    copy2(tmp, OUTPUT)
finally:
    tmp.unlink(missing_ok=True)

print(OUTPUT.resolve())
