import Navbar from "@/components/Navbar/navbar";
import { Toaster } from "@/components/ui/toast";

export default function ShopLayout({ children }: LayoutProps<"/">) {
  return (
    <>
      <Navbar />
      {children}
    </>
  );
}
