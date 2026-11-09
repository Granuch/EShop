import { Badge } from "@/components/ui/badge";
import type { AdminUser } from "@/lib/admin/types/identity";

/** Status badges in the order an admin cares about: deleted, then deactivated, then locked. */
function UserStateBadges({ user }: { user: AdminUser }) {
  return (
    <span className="flex flex-wrap gap-1">
      {user.isDeleted && <Badge variant="destructive">Deleted</Badge>}
      {!user.isDeleted && !user.isActive && <Badge variant="outline">Deactivated</Badge>}
      {user.isLockedOut && <Badge variant="outline">Locked</Badge>}
      {!user.emailConfirmed && <Badge variant="secondary">Unconfirmed</Badge>}
      {user.twoFactorEnabled && <Badge variant="secondary">2FA</Badge>}
    </span>
  );
}

export default UserStateBadges;
