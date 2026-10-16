// "1 spool", "2 spools". Pass `plural` for irregular nouns.
export function pluralize(count: number, noun: string, plural = `${noun}s`) {
  return `${count} ${count === 1 ? noun : plural}`;
}
