import { Injectable, signal } from '@angular/core';

type ThemeColor = 'primary' | 'secondary';

const DEFAULT_COLORS: Record<ThemeColor, string> = {
  primary: '#0e3a70',
  secondary: '#3eb838',
};
const THEME_STORAGE_KEY = 'hockey-season-theme';

/**
 * Application color theme (primary/secondary). Edited from the configuration
 * page, persisted in local storage and applied as CSS variables on the
 * document root.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly primaryColor = signal(DEFAULT_COLORS.primary);
  readonly secondaryColor = signal(DEFAULT_COLORS.secondary);
  readonly storageError = signal(false);

  constructor() {
    this.loadColors();
  }

  setColor(color: ThemeColor, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.applyColor(color, value);
    this.saveColors();
  }

  resetColors(): void {
    this.applyColor('primary', DEFAULT_COLORS.primary);
    this.applyColor('secondary', DEFAULT_COLORS.secondary);
    this.saveColors();
  }

  private applyColor(color: ThemeColor, value: string): void {
    (color === 'primary' ? this.primaryColor : this.secondaryColor).set(value);
    document.documentElement.style.setProperty(`--color-${color}`, value);
  }

  private loadColors(): void {
    try {
      const stored = localStorage.getItem(THEME_STORAGE_KEY);
      if (!stored) return;
      const colors: unknown = JSON.parse(stored);
      if (typeof colors !== 'object' || colors === null) throw new Error('Invalid saved theme');
      for (const color of ['primary', 'secondary'] as const) {
        const value = (colors as Record<ThemeColor, string>)[color];
        if (typeof value !== 'string' || !/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Invalid saved theme color');
      }
      const validColors = colors as Record<ThemeColor, string>;
      this.applyColor('primary', validColors.primary);
      this.applyColor('secondary', validColors.secondary);
    } catch (error) {
      console.error('Unable to load theme', error);
      this.storageError.set(true);
    }
  }

  private saveColors(): void {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify({
        primary: this.primaryColor(),
        secondary: this.secondaryColor(),
      }));
      this.storageError.set(false);
    } catch (error) {
      console.error('Unable to save theme', error);
      this.storageError.set(true);
    }
  }
}
