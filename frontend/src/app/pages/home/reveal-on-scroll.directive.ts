import { Directive, ElementRef, OnDestroy, OnInit, inject, signal } from '@angular/core';

@Directive({
  selector: '[appReveal]',
  host: {
    '[class.reveal]': 'true',
    '[class.is-visible]': 'visible()'
  }
})
export class RevealOnScrollDirective implements OnInit, OnDestroy {
  private readonly element = inject(ElementRef<HTMLElement>);
  private observer?: IntersectionObserver;
  protected readonly visible = signal(false);

  ngOnInit(): void {
    this.observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          this.visible.set(true);
          this.observer?.unobserve(this.element.nativeElement);
        }
      }
    }, { threshold: 0.15 });
    this.observer.observe(this.element.nativeElement);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
