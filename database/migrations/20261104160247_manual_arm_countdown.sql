-- Server-owned state: never expose aircraft command state via the Supabase Data API.
ALTER TABLE public."MissionAutomationState" ADD COLUMN IF NOT EXISTS "ExecutionJson" text;
ALTER TABLE public."MissionAutomationState" ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON public."MissionAutomationState" FROM anon, authenticated;
