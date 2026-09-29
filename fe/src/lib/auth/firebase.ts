"use client";

import { getApps, initializeApp } from "firebase/app";
import { initializeAuth, inMemoryPersistence, browserPopupRedirectResolver, type Auth } from "firebase/auth";

let auth: Auth | undefined;
export function getFirebaseAuth() {
  if (auth) return auth;
  const config = {
    apiKey: process.env.NEXT_PUBLIC_FIREBASE_API_KEY,
    authDomain: process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN,
    projectId: process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID,
    storageBucket: process.env.NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET,
    messagingSenderId: process.env.NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID,
    appId: process.env.NEXT_PUBLIC_FIREBASE_APP_ID,
  };
  if (!config.apiKey || !config.authDomain || !config.projectId || !config.appId) {
    throw new Error("Firebase web configuration is missing. Configure fe/.env.local.");
  }
  const app = getApps()[0] ?? initializeApp(config);
  // Never initialize with getAuth's default persistent browser storage.
  auth = initializeAuth(app, { persistence: inMemoryPersistence, popupRedirectResolver: browserPopupRedirectResolver });
  return auth;
}
