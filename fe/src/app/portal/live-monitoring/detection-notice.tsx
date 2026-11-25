import { TriangleAlert } from "lucide-react";
import styles from "./live-monitoring.module.css";

export default function DetectionNotice({ messages }: { messages?: string[] | null }) {
  return <div role="status" aria-atomic="true" className={styles.noticeRegion}>
    {Boolean(messages?.length) && <div className={styles.detectionNotice}>
      <TriangleAlert aria-hidden="true" size={22} />
      <div>
        <h2>Missing data for automated checks</h2>
        <ul>{messages?.map(message => <li key={message}>{message}</li>)}</ul>
      </div>
    </div>}
  </div>;
}
