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
};

@Component({
  selector: 'cp-icon',
  template: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path [attr.d]="path" /></svg>',
})
export class Icon {
  @Input() name = 'sales';
  get path(): string { return paths[this.name] ?? paths['sales']; }
}
