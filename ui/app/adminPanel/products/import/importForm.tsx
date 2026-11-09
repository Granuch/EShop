"use client";

import Link from "next/link";
import { useActionState } from "react";
import ActionButton from "@/components/Admin/actionButton";
import { FormMessage } from "@/components/Admin/formMessage";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { IDLE } from "@/lib/admin/forms";
import { importProductsAction, publishImportedAction, type ImportState } from "./actions";

/** Upload or paste, then the per-row report (a 200 is not "every row was created"), then optionally publish them. */
function ImportForm() {
  const [state, formAction, pending] = useActionState<ImportState, FormData>(importProductsAction, IDLE);
  const created = state.report?.rows.filter((row) => row.succeeded && row.productId).map((row) => row.productId!) ?? [];

  return (
    <div className="space-y-6">
      <form action={formAction} className="max-w-2xl space-y-4" aria-label="Import products">
        <div className="space-y-1.5">
          <Label htmlFor="file">CSV file</Label>
          <input
            id="file"
            name="file"
            type="file"
            accept=".csv,text/csv"
            className="block text-sm file:mr-3 file:rounded-md file:border file:bg-background file:px-2.5 file:py-1 file:text-sm"
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="csv">…or paste it</Label>
          <Textarea id="csv" name="csv" rows={6} className="font-mono text-xs" placeholder="Sku,Name,Description,CategoryId,Price,StockQuantity" />
        </div>
        <Button type="submit" disabled={pending}>
          {pending ? "Importing…" : "Import"}
        </Button>
      </form>

      {state.message && !state.report && !state.rowErrors && <FormMessage state={state} />}

      {state.rowErrors && (
        <div role="alert" className="space-y-1 text-sm text-destructive">
          <p>{state.message}</p>
          <ul className="list-disc pl-5">
            {state.rowErrors.map((error) => (
              <li key={error.line}>{`Row ${error.line}: ${error.message}`}</li>
            ))}
          </ul>
        </div>
      )}

      {state.report && (
        <section aria-labelledby="import-report" className="space-y-3">
          <h2 id="import-report" className="text-sm font-semibold">
            Report
          </h2>
          <p
            role={state.status === "error" ? "alert" : "status"}
            className={state.status === "error" ? "text-sm text-destructive" : "text-sm text-emerald-700 dark:text-emerald-400"}
          >
            {state.message}
          </p>
          {created.length > 0 && (
            <ActionButton action={publishImportedAction.bind(null, created)} variant="default" size="default" align="start">
              {`Publish the ${created.length} created`}
            </ActionButton>
          )}
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Row</TableHead>
                <TableHead>SKU</TableHead>
                <TableHead>Result</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {state.report.rows.map((row) => (
                <TableRow key={row.index}>
                  <TableCell className="tabular-nums">{state.lines?.[row.index] ?? row.index + 2}</TableCell>
                  <TableCell className="font-mono text-xs">{row.sku}</TableCell>
                  <TableCell className="whitespace-normal">
                    {row.succeeded && row.productId ? (
                      <Link href={`/adminPanel/products/${row.productId}`} className="underline">
                        Created
                      </Link>
                    ) : (
                      <span className="text-destructive">{row.error ?? row.errorCode}</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </section>
      )}
    </div>
  );
}

export default ImportForm;
