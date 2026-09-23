import type { Locator } from '@playwright/test'

/**
 * On the mobile-emulated Chromium project, Chromium's dynamic-toolbar viewport simulation reports
 * a layout viewport taller than the one clicks are actually dispatched against once the page has
 * scrolled. Playwright's own click always re-scrolls the target into view first, which then
 * computes the click point against the wrong (taller) viewport, so the click lands on whatever
 * element sits at that offset — a sibling card, a dialog's own header — instead of the target.
 * Keyboard activation sidesteps mouse coordinates entirely; it is what a real user tabbing to the
 * button would trigger, so it is a faithful fallback rather than a workaround around real behaviour.
 */
export async function clickReliably(locator: Locator): Promise<void> {
  try {
    await locator.click({ timeout: 3000 })
  } catch {
    await locator.focus()
    await locator.press('Enter')
  }
}

/**
 * As {@link clickReliably}, but for a checkbox or radio input: native inputs of that type toggle on
 * Space, not Enter, when activated from the keyboard.
 */
export async function toggleReliably(locator: Locator): Promise<void> {
  try {
    await locator.click({ timeout: 3000 })
  } catch {
    await locator.focus()
    await locator.press(' ')
  }
}
