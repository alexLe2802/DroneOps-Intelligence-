"use client";

import { Bell, CheckCheck } from "lucide-react";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { getNotificationFeed, markAllNotificationsRead, markNotificationRead } from "@/lib/portal/client";
import type { NotificationFeed, PortalNotification } from "@/lib/portal/types";
import { notificationDestination } from "./notification-route";
import "./notification-center.css";

const emptyFeed: NotificationFeed = { unreadCount: 0, items: [] };

function relativeTime(value: string): string {
  const timestamp = new Date(value).getTime();
  if (!Number.isFinite(timestamp)) return "UNKNOWN TIME";
  const seconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
  if (seconds < 60) return "JUST NOW";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}M AGO`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}H AGO`;
  const days = Math.floor(hours / 24);
  return days < 7 ? `${days}D AGO` : new Date(value).toLocaleDateString();
}

export default function NotificationCenter() {
  const router = useRouter();
  const root = useRef<HTMLDivElement>(null);
  const mounted = useRef(false);
  const [open, setOpen] = useState(false);
  const [feed, setFeed] = useState<NotificationFeed>(emptyFeed);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState("");
  const [busyId, setBusyId] = useState("");

  const load = useCallback(async () => {
    try {
      const result = await getNotificationFeed(false, 50);
      if (!mounted.current) return;
      setFeed(result);
      setError("");
    } catch (reason) {
      if (mounted.current) setError(reason instanceof Error ? reason.message : "Notifications are unavailable.");
    } finally {
      if (mounted.current) setLoaded(true);
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    const initialLoad = window.setTimeout(() => void load(), 0);
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 30000);
    const refresh = () => { if (document.visibilityState === "visible") void load(); };
    document.addEventListener("visibilitychange", refresh);
    return () => {
      mounted.current = false;
      window.clearTimeout(initialLoad);
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", refresh);
    };
  }, [load]);

  useEffect(() => {
    if (!open) return;
    const close = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("pointerdown", close);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", close);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);

  function setRead(id: string) {
    setFeed(current => ({
      unreadCount: Math.max(0, current.unreadCount - (current.items.some(item => item.id === id && !item.isRead) ? 1 : 0)),
      items: current.items.map(item => item.id === id ? { ...item, isRead: true, readAt: item.readAt ?? new Date().toISOString() } : item),
    }));
  }

  async function inspect(notification: PortalNotification) {
    if (busyId) return;
    setBusyId(notification.id);
    if (!notification.isRead) {
      try {
        await markNotificationRead(notification.id);
        setRead(notification.id);
      } catch {
        // Navigation remains available; unread state retries on the next refresh.
      }
    }
    setBusyId("");
    setOpen(false);
    router.push(notificationDestination(notification));
  }

  async function markAllRead() {
    if (busyId || feed.unreadCount === 0) return;
    setBusyId("all");
    try {
      await markAllNotificationsRead();
      setFeed(current => ({
        unreadCount: 0,
        items: current.items.map(item => ({ ...item, isRead: true, readAt: item.readAt ?? new Date().toISOString() })),
      }));
      setError("");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to mark notifications as read.");
    } finally {
      setBusyId("");
    }
  }

  return <div className="notification-center" ref={root}>
    <button type="button" className="notification-trigger" aria-label={`Notifications${feed.unreadCount ? `, ${feed.unreadCount} unread` : ""}`} aria-expanded={open} aria-controls="notification-panel" onClick={() => { setOpen(value => !value); if (!open) void load(); }}>
      <Bell size={17} strokeWidth={1.8} aria-hidden="true" />
      {feed.unreadCount > 0 && <span>{feed.unreadCount > 99 ? "99+" : feed.unreadCount}</span>}
    </button>
    {open && <section id="notification-panel" className="notification-panel" aria-label="Notifications">
      <header><div><span>OPERATIONS FEED</span><h2>Notifications</h2></div><button type="button" disabled={busyId !== "" || feed.unreadCount === 0} onClick={() => void markAllRead()}><CheckCheck size={15} aria-hidden="true" /> MARK ALL READ</button></header>
      {error && <div className="notification-error" role="alert"><span>{error}</span><button type="button" onClick={() => void load()}>RETRY</button></div>}
      <div className="notification-list">
        {!loaded ? <p className="notification-empty">Loading notifications...</p> : feed.items.length === 0 ? <p className="notification-empty">No notifications yet.</p> : feed.items.map(notification => <button type="button" key={notification.id} className={`notification-item${notification.isRead ? " is-read" : " is-unread"}`} disabled={busyId !== ""} onClick={() => void inspect(notification)}>
          <i aria-hidden="true" />
          <span><strong>{notification.title}</strong><small>{notification.message}</small><time title={new Date(notification.createdAt).toLocaleString()}>{relativeTime(notification.createdAt)}{notification.isRead ? " / SEEN" : " / NEW"}</time></span>
        </button>)}
      </div>
      <footer><span>{feed.items.length} RECENT</span><b>{feed.unreadCount} UNREAD</b></footer>
    </section>}
  </div>;
}
