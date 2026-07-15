export function formatWeightGrams(grams: number): string {
  if (grams === 1000) {
    return '1 kg';
  }
  return `${grams} g`;
}
