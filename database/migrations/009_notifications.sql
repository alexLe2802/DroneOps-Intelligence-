CREATE TABLE IF NOT EXISTS public."Notification" (
    "Id" uuid PRIMARY KEY,
    "RecipientUserId" uuid NOT NULL REFERENCES public."User"("Id") ON DELETE CASCADE,
    "ActorUserId" uuid NULL REFERENCES public."User"("Id") ON DELETE SET NULL,
    "Type" text NOT NULL,
    "Title" text NOT NULL,
    "Message" text NOT NULL,
    "ReferenceId" uuid NULL,
    "ReferenceType" text NULL,
    "ActionUrl" text NULL,
    "IsRead" boolean NOT NULL DEFAULT false,
    "ReadAt" timestamp with time zone NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_Notification_Recipient_Read_Created"
    ON public."Notification" ("RecipientUserId", "IsRead", "CreatedAt" DESC);

-- At most one aggregate, active manual-review notification per manager.
-- Closed queue notifications are retained as history and are outside this index.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Notification_UavManualReview_Active"
    ON public."Notification" ("RecipientUserId")
    WHERE "Type" = 'UavManualReview'
      AND "ReferenceType" = 'UAVManualReviewQueueActive';
