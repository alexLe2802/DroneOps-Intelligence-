export type NavItem = {
  icon: string;
  label: string;
  href: string;
};

export type NavGroup = {
  section: string;
  items: NavItem[];
};

export const managerNavGroups: NavGroup[] = [
  {
    section: "Missions",
    items: [
      { icon: "dashboard", label: "Dashboard", href: "/portal" },
      { icon: "planning", label: "Mission Planning", href: "/portal/mission-planning" },
      { icon: "approvals", label: "Validation & Approvals", href: "/portal/mission-approvals" },
    ],
  },
  {
    section: "Operations",
    items: [
      { icon: "monitoring", label: "Live Monitoring", href: "/portal/live-monitoring" },
      { icon: "uav", label: "UAV Management", href: "/portal/uav-management" },
      { icon: "geofences", label: "Fleet & Restrictions", href: "/portal/geofences" },
      { icon: "operators", label: "Operators", href: "/portal/operators" },
      { icon: "incidents", label: "Incident Management", href: "/portal/incidents" },
    ],
  },
  {
    section: "Review",
    items: [
      { icon: "history", label: "Flight History & Reports", href: "/portal/flight-history" },
      { icon: "ai", label: "AI Risk Assessment", href: "/portal/ai-assessment" },
    ],
  },
];

export const operatorNavGroups: NavGroup[] = [
  {
    section: "Missions",
    items: [
      { icon: "dashboard", label: "My Dashboard", href: "/portal" },
      { icon: "missions", label: "My Missions", href: "/portal/missions" },
      { icon: "validation", label: "Validation Results", href: "/portal/validation-results" },
    ],
  },
  {
    section: "Operations",
    items: [
      { icon: "monitoring", label: "Live Monitoring", href: "/portal/live-monitoring" },
      { icon: "uav", label: "My UAVs", href: "/portal/uav" },
      { icon: "connection", label: "Connect Drone", href: "/portal/drone-connection" },
      { icon: "incidents", label: "My Incidents", href: "/portal/incidents" },
    ],
  },
  {
    section: "Review",
    items: [
      { icon: "history", label: "Flight History", href: "/portal/flight-history" },
      { icon: "ai", label: "AI Risk Assessment", href: "/portal/ai-assessment" },
    ],
  },
];

export const managerNavItems = managerNavGroups.flatMap(group => group.items.map(item => [item.icon, item.label, ""] as const));
export const operatorNavItems = operatorNavGroups.flatMap(group => group.items.map(item => [item.icon, item.label, ""] as const));
