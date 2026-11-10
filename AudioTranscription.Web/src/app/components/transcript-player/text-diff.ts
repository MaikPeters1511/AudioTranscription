export type DiffKind = 'same' | 'added' | 'removed';

export interface DiffPart {
  text: string;
  kind: DiffKind;
}

/**
 * Word-level diff (longest common subsequence) between two versions of a segment, used to highlight
 * what a correction changed (S32). Whitespace is kept as part of the tokens so the parts can be
 * concatenated back to either version.
 */
export function diffWords(from: string, to: string): DiffPart[] {
  const a = tokenize(from);
  const b = tokenize(to);

  // lcs[i][j] = length of the longest common subsequence of a[i..] and b[j..]
  const lcs: number[][] = Array.from({ length: a.length + 1 }, () => new Array<number>(b.length + 1).fill(0));
  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    }
  }

  const parts: DiffPart[] = [];
  const push = (text: string, kind: DiffKind) => {
    const last = parts[parts.length - 1];
    if (last?.kind === kind) {
      last.text += text;
    } else {
      parts.push({ text, kind });
    }
  };

  let i = 0;
  let j = 0;
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) {
      push(a[i], 'same');
      i++;
      j++;
    } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
      push(a[i++], 'removed');
    } else {
      push(b[j++], 'added');
    }
  }
  while (i < a.length) {
    push(a[i++], 'removed');
  }
  while (j < b.length) {
    push(b[j++], 'added');
  }
  return parts;
}

function tokenize(text: string): string[] {
  return text.split(/(\s+)/).filter((token) => token.length > 0);
}
