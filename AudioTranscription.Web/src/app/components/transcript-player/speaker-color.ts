/**
 * Deterministic DaisyUI badge class per speaker index (S11). Color alone never carries the
 * information (WCAG 1.4.1): the speaker's name is always rendered alongside the badge.
 */
const PALETTE = ['badge-primary', 'badge-secondary', 'badge-accent', 'badge-info', 'badge-success', 'badge-warning'];

export function speakerBadgeClass(index: number): string {
  return PALETTE[index % PALETTE.length];
}
