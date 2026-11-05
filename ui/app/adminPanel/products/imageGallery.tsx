import { ChevronLeft, ChevronRight, Star, Trash2 } from "lucide-react";
import ActionButton from "@/components/Admin/actionButton";
import type { ProductImage } from "@/lib/admin/types/catalog";
import {
  addImageAction,
  deleteImageAction,
  moveImageAction,
  setMainImageAction,
  updateImageAction,
} from "./actions";
import ImageForm from "./imageForm";

const MAX_IMAGES = 10;

type ImageGalleryProps = {
  productId: string;
  /** As the API returns them: by displayOrder, then age. */
  images: ProductImage[];
  canWrite: boolean;
};

/**
 * The product's gallery. The API stores URLs only (no upload), at most 10, exactly one main image. Writers can add,
 * edit, delete, make main and move; readers see the images only.
 */
function ImageGallery({ productId, images, canWrite }: ImageGalleryProps) {
  return (
    <section aria-labelledby="images-heading" className="space-y-3">
      <h2 id="images-heading" className="text-sm font-semibold">
        Images <span className="font-normal text-muted-foreground">{`${images.length} of ${MAX_IMAGES}`}</span>
      </h2>
      {images.length === 0 ? (
        <p className="text-sm text-muted-foreground">No images</p>
      ) : (
        <ol className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
          {images.map((image, index) => (
            <li key={image.id} className="space-y-2" aria-label={`Image ${index + 1}${image.isMain ? ", main" : ""}`}>
              {/* A plain <img>: images may come from any host; next.config only allows picsum (PLAN Q7). */}
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={image.url}
                alt={image.altText ?? ""}
                loading="lazy"
                className="aspect-square w-full rounded-lg bg-muted object-cover ring-1 ring-foreground/10"
              />
              <p className="truncate text-xs text-muted-foreground" title={image.url}>
                {image.isMain && <span className="font-medium text-foreground">Main · </span>}
                {image.altText || "No alt text"}
              </p>
              {canWrite && (
                <>
                  <div className="flex flex-wrap items-start gap-1">
                    <ActionButton
                      action={moveImageAction.bind(null, productId, image.id, -1)}
                      label="Move earlier"
                      size="icon-sm"
                      variant="ghost"
                      align="start"
                      disabled={index === 0}
                    >
                      <ChevronLeft aria-hidden />
                    </ActionButton>
                    <ActionButton
                      action={moveImageAction.bind(null, productId, image.id, 1)}
                      label="Move later"
                      size="icon-sm"
                      variant="ghost"
                      align="start"
                      disabled={index === images.length - 1}
                    >
                      <ChevronRight aria-hidden />
                    </ActionButton>
                    {!image.isMain && (
                      <ActionButton
                        action={setMainImageAction.bind(null, productId, image.id)}
                        label="Make main image"
                        size="icon-sm"
                        variant="ghost"
                        align="start"
                      >
                        <Star aria-hidden />
                      </ActionButton>
                    )}
                    <ActionButton
                      action={deleteImageAction.bind(null, productId, image.id)}
                      label="Delete image"
                      size="icon-sm"
                      variant="ghost"
                      align="start"
                      confirm={{
                        title: "Delete this image?",
                        description: image.isMain
                          ? "It is the main image: the next image in the gallery becomes main. This cannot be undone."
                          : "This cannot be undone; the URL can be added again later.",
                        confirmLabel: "Delete image",
                      }}
                    >
                      <Trash2 aria-hidden />
                    </ActionButton>
                  </div>
                  <details className="text-sm">
                    <summary className="cursor-pointer text-xs underline">Edit URL or alt text</summary>
                    <div className="mt-2">
                      <ImageForm
                        action={updateImageAction.bind(null, productId, image.id)}
                        initial={{ url: image.url, altText: image.altText ?? "" }}
                        submitLabel="Save image"
                        ariaLabel={`Edit image ${index + 1}`}
                        note="Both fields are replaced: an empty alt text clears it."
                      />
                    </div>
                  </details>
                </>
              )}
            </li>
          ))}
        </ol>
      )}
      {canWrite &&
        (images.length < MAX_IMAGES ? (
          <div className="max-w-md rounded-lg border p-4">
            <h3 className="mb-3 text-sm font-medium">Add an image</h3>
            <ImageForm
              action={addImageAction.bind(null, productId)}
              submitLabel="Add image"
              ariaLabel="Add image"
              note={images.length === 0 ? "The first image becomes the main image." : "It is added at the end."}
            />
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">The gallery is full: delete an image to add another.</p>
        ))}
    </section>
  );
}

export default ImageGallery;
