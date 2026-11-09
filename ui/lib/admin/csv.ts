/**
 * A small RFC 4180 reader for the files this admin produces and reads back (the product export): quoted fields,
 * doubled quotes, CR/LF/CRLF line ends, a leading BOM. Returns rows of raw strings; empty lines are dropped.
 */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = "";
  let quoted = false;
  let i = text.charCodeAt(0) === 0xfeff ? 1 : 0;

  const endField = () => {
    row.push(field);
    field = "";
  };
  const endRow = () => {
    endField();
    if (row.length > 1 || row[0] !== "") rows.push(row);
    row = [];
  };

  for (; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          quoted = false;
        }
      } else {
        field += c;
      }
    } else if (c === '"') {
      quoted = true;
    } else if (c === ",") {
      endField();
    } else if (c === "\r" || c === "\n") {
      if (c === "\r" && text[i + 1] === "\n") i++;
      endRow();
    } else {
      field += c;
    }
  }
  if (field !== "" || row.length > 0) endRow();
  return rows;
}

/**
 * Undoes the export's CSV-injection guard: SafeCsv prefixes an apostrophe to a field starting with = + - @ tab or CR,
 * so "'-5" in a file the export wrote stands for "-5". Any other leading apostrophe is the user's own and kept.
 */
export function undoFormulaGuard(value: string): string {
  return value.length > 1 && value[0] === "'" && "=+-@\t\r".includes(value[1]) ? value.slice(1) : value;
}
