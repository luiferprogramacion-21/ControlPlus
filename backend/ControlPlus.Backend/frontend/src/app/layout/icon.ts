import { Component, Input } from '@angular/core';

const paths: Record<string, string> = {
  menu: 'M4 6h16 M4 12h16 M4 18h16',
  close: 'M6 6l12 12 M6 18L18 6',
  dashboard: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  sales: 'M5 3h14v18l-3-2-4 2-4-2-3 2V3z M8 7h8 M8 11h8 M8 15h5',
  cash: 'M3 6h18v14H3z M3 10h18 M8 15h3 M17 3v3',
  catalog: 'M3 3h9l9 9-9 9-9-9V3z M7 7h.01',
  inventory: 'M3 4h18v17H3z M3 9h18 M12 9v12',
  customers: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a6 6 0 0 1 6-6h4a6 6 0 0 1 6 6v2',
  purchases: 'M2 3h3l3 12h11l3-9H6 M9 20h.01 M18 20h.01',
  reports: 'M4 21V12h4v9 M10 21V7h4v14 M16 21V3h4v18 M2 21h20',
  settings: 'M12 3l8 4v10l-8 4-8-4V7l8-4z M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0',
  lock: 'M5 10h14v11H5z M8 10V7a4 4 0 0 1 8 0v3 M12 14v3',
  history: 'M12 7v5l3 2 M3.5 15a9 9 0 1 0 1-7.5 M3 4v4h4',
  layaway: 'M5 8h14l-1 13H6L5 8z M9 8V6a3 3 0 0 1 6 0v2',
  credit: 'M3 6h18v12H3z M3 10h18 M7 15h4',
  labels: 'M4 5v14 M7 5v14 M10 5v14 M14 5v14 M16 5v14 M20 5v14',
  reception: 'M3 8l9-5 9 5v9l-9 5-9-5V8z M12 8v7 M9 12l3 3 3-3',
  supplier: 'M2 6h11v10H2z M13 10h4l3 3v3h-7 M8 18a2 2 0 1 1-4 0 2 2 0 0 1 4 0 M19 18a2 2 0 1 1-4 0 2 2 0 0 1 4 0',
  admin: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3z M9 12l2 2 4-4',
  'text-size': 'M3 18l5-12 5 12 M5 14h6 M14 18l3.5-8 3.5 8 M15.2 15.5h4.6',
  logout: 'M10 4H5v16h5 M15 8l4 4-4 4 M19 12H9',
  'chevron-left': 'M15 6l-6 6 6 6',
  'chevron-right': 'M9 6l6 6-6 6',
  check: 'M5 12l5 5 9-10',
  keyboard: 'M2 6h20v12H2z M6 10h.01 M10 10h.01 M14 10h.01 M18 10h.01 M7 14h10',
};

@Component({
  selector: 'cp-icon',
  template: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path [attr.d]="path" /></svg>',
})
export class Icon {
  @Input() name = 'sales';
  get path(): string { return paths[this.name] ?? paths['sales']; }
}
