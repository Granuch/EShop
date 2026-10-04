import Navbar from "@/components/Navbar/navbar";

export default function ShopLayout({ children }: LayoutProps<"/">) {
  return (
    <>
      <Navbar />
      {children}
    </>
  );
}
