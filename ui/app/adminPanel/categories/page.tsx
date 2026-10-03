import Link from "next/link";
import { ChevronDown, ChevronUp, Plus, RotateCcw } from "lucide-react";
import AccessDenied from "@/components/Admin/accessDenied";
import ActionButton from "@/components/Admin/actionButton";
import PageHeader from "@/components/Admin/pageHeader";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { classifyFailure } from "@/lib/admin/api";
import { getAdminSession, hasPermission } from "@/lib/admin/auth";
import { getCategoryTree } from "@/lib/admin/catalog";
import { ADMIN_ROLE_HINTS } from "@/lib/admin/permissions";
import type { Category } from "@/lib/admin/types/catalog";
import { cn } from "@/lib/utils";
import { moveCategoryOrderAction, restoreCategoryAction } from "./actions";

export const metadata = { title: "Categories · Admin · EShop" };

const CATEGORIES_PATH = "/adminPanel/categories";

type LevelProps = { nodes: Category[]; parentId: string | null; canWrite: boolean; depth: number };

/** One level of the tree, in the API's order (displayOrder, then name). Deleted categories are listed, marked. */
function Level({ nodes, parentId, canWrite, depth }: LevelProps) {
  const live = nodes.filter((node) => node.isActive);
  return (
    <ul className={cn("space-y-1", depth > 0 && "mt-1 border-l pl-4")}>
      {nodes.map((node) => {
        const position = live.indexOf(node);
        return (
          <li key={node.id}>
            <div className="flex flex-wrap items-center gap-2 rounded-md px-2 py-1 hover:bg-muted/50">
              <Link
                href={`${CATEGORIES_PATH}/${node.id}`}
                className={cn("font-medium hover:underline", !node.isActive && "text-muted-foreground line-through")}
              >
                {node.name}
              </Link>
              <span className="font-mono text-xs text-muted-foreground">{node.slug}</span>
              {!node.isActive && <Badge variant="outline">Deleted</Badge>}
              <span className="ml-auto flex items-center gap-1">
                {canWrite && node.isActive && (
                  <>
                    <ActionButton
                      action={moveCategoryOrderAction.bind(null, parentId, node.id, -1)}
                      label={`Move ${node.name} up`}
                      size="icon-sm"
                      variant="ghost"
                      disabled={position === 0}
                    >
                      <ChevronUp aria-hidden />
                    </ActionButton>
                    <ActionButton
                      action={moveCategoryOrderAction.bind(null, parentId, node.id, 1)}
                      label={`Move ${node.name} down`}
                      size="icon-sm"
                      variant="ghost"
                      disabled={position === live.length - 1}
                    >
                      <ChevronDown aria-hidden />
                    </ActionButton>
                  </>
                )}
                {canWrite && !node.isActive && (
                  <ActionButton action={restoreCategoryAction.bind(null, node.id)} label={`Restore ${node.name}`}>
                    <RotateCcw aria-hidden data-icon="inline-start" />
                    Restore
                  </ActionButton>
                )}
              </span>
            </div>
            {node.childCategories.length > 0 && (
              <Level nodes={node.childCategories} parentId={node.id} canWrite={canWrite} depth={depth + 1} />
            )}
          </li>
        );
      })}
    </ul>
  );
}

function count(nodes: Category[]): { live: number; deleted: number } {
  return nodes.reduce(
    (sum, node) => {
      const below = count(node.childCategories);
      return {
        live: sum.live + below.live + (node.isActive ? 1 : 0),
        deleted: sum.deleted + below.deleted + (node.isActive ? 0 : 1),
      };
    },
    { live: 0, deleted: 0 },
  );
}

export default async function CategoriesPage() {
  const session = await getAdminSession();
  if (!hasPermission(session, "catalog.read")) return <AccessDenied />;

  let tree: Category[];
  try {
    tree = await getCategoryTree();
  } catch (error) {
    const failure = classifyFailure(error);
    if (failure?.kind === "forbidden") return <AccessDenied hint={ADMIN_ROLE_HINTS.catalog} />;
    if (failure?.kind === "rateLimited") {
      return (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{failure.message}</AlertDescription>
        </Alert>
      );
    }
    throw error;
  }
  const canWrite = hasPermission(session, "catalog.write");
  const totals = count(tree);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Categories"
        description={`${totals.live} live${totals.deleted ? `, ${totals.deleted} deleted` : ""}. Order is the shop's order; arrows move a category within its level.`}
      >
        {canWrite && (
          <Link href={`${CATEGORIES_PATH}/new`} className={cn(buttonVariants())}>
            <Plus aria-hidden data-icon="inline-start" />
            New category
          </Link>
        )}
      </PageHeader>

      {!session.roles.includes("Admin") && (
        <Alert>
          <AlertDescription>Deleted categories are listed only for the Admin role.</AlertDescription>
        </Alert>
      )}

      {tree.length === 0 ? (
        <p className="mt-12 text-center font-medium">No categories yet</p>
      ) : (
        <nav aria-label="Category tree" className="max-w-3xl">
          <Level nodes={tree} parentId={null} canWrite={canWrite} depth={0} />
        </nav>
      )}
    </div>
  );
}
