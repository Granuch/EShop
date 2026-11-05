type PageHeaderProps = {
  title: string;
  description?: string;
  /** Actions shown at the right of the title, e.g. a "New product" button. */
  children?: React.ReactNode;
};

function PageHeader({ title, description, children }: PageHeaderProps) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      </div>
      {children && <div className="flex flex-wrap items-center gap-2">{children}</div>}
    </div>
  );
}

export default PageHeader;
