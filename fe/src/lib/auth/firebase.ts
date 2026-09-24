"use client";

import { getApps, initializeApp } from "firebase/app";
import { initializeAuth, inMemoryPersistence, browserPopupRedirectResolver, type Auth } from "firebase/auth";

let auth: Auth | undefined;
export function getFirebaseAuth() {
  if (auth) return auth;
  const app = getApps()[0] ?? initializeApp({
    apiKey: process.env.NEXT_PUBLIC_FIREBASE_API_KEY ?? "AIzaSyA9tb18CSNenlCh_ZjVffYf5UIIiCxY6YA",
    authDomain: process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN ?? "droneops-intelligence.firebaseapp.com",
    projectId: process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID ?? "droneops-intelligence",
    storageBucket: "droneops-intelligence.firebasestorage.app",
    messagingSenderId: "899152835755",
    appId: "1:899152835755:web:7e867ed9293841c13433ee",
  });
  // Never initialize with getAuth's default persistent browser storage.
  auth = initializeAuth(app, { persistence: inMemoryPersistence, popupRedirectResolver: browserPopupRedirectResolver });
  return auth;
}
