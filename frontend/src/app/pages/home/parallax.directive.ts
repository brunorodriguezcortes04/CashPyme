import { Directive, DestroyRef, ElementRef, OnInit, inject, input, signal } from '@angular/core';

@Directive({
  selector: '[appParallax]',
  host: {
    '[style.transform]': 'transform()'
  }
})
export class ParallaxDirective implements OnInit {
  private readonly element = inject(ElementRef<HTMLElement>);
  private readonly destroyRef = inject(DestroyRef);
  readonly appParallax = input(0.12);
  protected readonly transform = signal('translateY(0px)');

  ngOnInit(): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      return;
    }

    let ticking = false;
    const update = () => {
      const offset = this.element.nativeElement.getBoundingClientRect().top * this.appParallax();
      this.transform.set(`translateY(${-offset}px)`);
      ticking = false;
    };
    const onScroll = () => {
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(update);
      }
    };

    update();
    window.addEventListener('scroll', onScroll, { passive: true });
    this.destroyRef.onDestroy(() => window.removeEventListener('scroll', onScroll));
  }
}
