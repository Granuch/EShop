import { Trash2 } from "lucide-react";
import ActionButton from "@/components/Admin/actionButton";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import type { ProductAttribute } from "@/lib/admin/types/catalog";
import { addAttributeAction, deleteAttributeAction, replaceAttributesAction, updateAttributeAction } from "./actions";
import { AttributeForm, AttributesTextForm } from "./attributeForms";

const MAX_ATTRIBUTES = 50;

type AttributesCardProps = {
  productId: string;
  attributes: ProductAttribute[];
  canWrite: boolean;
};

/** The API keeps no order, so they are shown by name. Writers edit one at a time, or the whole set as text. */
function AttributesCard({ productId, attributes, canWrite }: AttributesCardProps) {
  const sorted = [...attributes].sort((a, b) => a.name.localeCompare(b.name));

  return (
    <Card>
      <CardHeader>
        <CardTitle>Attributes</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {sorted.length === 0 ? (
          <p className="text-sm text-muted-foreground">No attributes</p>
        ) : (
          <dl className="divide-y">
            {sorted.map((attribute) => (
              <div key={attribute.id} className="py-1.5 text-sm">
                <div className="flex items-start justify-between gap-4">
                  <dt className="text-muted-foreground [overflow-wrap:anywhere]">{attribute.name}</dt>
                  <dd className="flex items-start gap-1 text-right [overflow-wrap:anywhere]">
                    {attribute.value}
                    {canWrite && (
                      <ActionButton
                        action={deleteAttributeAction.bind(null, productId, attribute.id)}
                        label={`Remove ${attribute.name}`}
                        size="icon-sm"
                        variant="ghost"
                      >
                        <Trash2 aria-hidden />
                      </ActionButton>
                    )}
                  </dd>
                </div>
                {canWrite && (
                  <details className="mt-1">
                    <summary className="cursor-pointer text-xs underline">Edit</summary>
                    <div className="mt-2">
                      <AttributeForm
                        action={updateAttributeAction.bind(null, productId, attribute.id)}
                        initial={{ name: attribute.name, value: attribute.value }}
                        submitLabel="Save attribute"
                        ariaLabel={`Edit ${attribute.name}`}
                      />
                    </div>
                  </details>
                )}
              </div>
            ))}
          </dl>
        )}

        {canWrite && (
          <>
            {attributes.length < MAX_ATTRIBUTES ? (
              <div className="border-t pt-4">
                <AttributeForm action={addAttributeAction.bind(null, productId)} submitLabel="Add attribute" ariaLabel="Add attribute" />
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">50 attributes is the maximum.</p>
            )}
            <details className="border-t pt-4 text-sm">
              <summary className="cursor-pointer text-xs underline">Edit all as text</summary>
              <div className="mt-2">
                <AttributesTextForm
                  action={replaceAttributesAction.bind(null, productId)}
                  initial={sorted.map((attribute) => `${attribute.name}: ${attribute.value}`).join("\n")}
                />
              </div>
            </details>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default AttributesCard;
