(()=>{if(!window.gsap||!window.ScrollTrigger||!window.ScrollSmoother)return;const root=document.querySelector('main');const reduced=matchMedia('(prefers-reduced-motion: reduce)');const finishHeroIntro=v=>{if(v&&!reduced.matches)v.play().catch(()=>{});};
const aliases={'.editorial-copy':'intro','.program-section':'venue-promotions','.program-posters':'promotion-grid','.program-mosaic':'about-teaser-mosaic','.mosaic-picture':'mosaic-item','.heritage-page':'spyker-heritage-page','.heritage-track':'heritage-scroll-track','.heritage-copy':'heritage-text-frame','.heritage-copy h2':'heritage-milestone-title','.heritage-copy p':'heritage-milestone-copy','.heritage-copy strong':'heritage-giant-year','.heritage-copy > span':'heritage-chapter-tag','.heritage-end':'heritage-end-frame','.hero:first-child > video':'hero-image','.feature-grid':'editorial'};Object.entries(aliases).forEach(([selector,name])=>root.querySelectorAll(selector).forEach(el=>el.classList.add(name)));
const statement=root.querySelectorAll('.hero')[1];if(statement){statement.classList.add('statement');const frame=document.createElement('div');frame.className='scroll-scale-frame';while(statement.firstChild)frame.append(statement.firstChild);statement.append(frame);frame.querySelector('video')?.classList.add('background-video');}
const contact=root.querySelector('.contact-grid');if(contact){gsap.matchMedia().add('(prefers-reduced-motion: no-preference)',()=>{gsap.from(contact.querySelectorAll('section'),{y:26,opacity:0,duration:.85,stagger:.08,ease:'power3.out',delay:.08});});}
const reservation=root.querySelector('#reservation-form');if(reservation){const title=root.querySelector('h1');title?.classList.add('reservation-title');}
const heritage=root.querySelector('.heritage-page');if(heritage){const container=document.createElement('div');container.className='heritage-horizontal-container';const track=heritage.querySelector('.heritage-track');track.before(container);container.append(track);track.querySelectorAll('.heritage-group > img').forEach(img=>{const frame=document.createElement('div');frame.className='heritage-image-frame';img.before(frame);frame.append(img);img.classList.add('heritage-parallax-img');});}
root.querySelectorAll('.feature-grid article').forEach(article=>{const fig=document.createElement('figure');article.replaceWith(fig);fig.append(article);const img=article.querySelector('img'),frame=document.createElement('div');frame.className='photo-frame';img.before(frame);frame.append(img);});
root.querySelectorAll('h1,h2').forEach(h=>{const walk=document.createTreeWalker(h,NodeFilter.SHOW_TEXT),texts=[];while(walk.nextNode())texts.push(walk.currentNode);texts.forEach(text=>{const fragment=document.createDocumentFragment();text.textContent.split(/(\s+)/).forEach(word=>{if(!word.trim()){fragment.append(document.createTextNode(word));return;}const wrap=document.createElement('span'),inner=document.createElement('span');wrap.className='split-word-wrap';inner.className='split-word';inner.textContent=word;wrap.append(inner);fragment.append(wrap);});text.replaceWith(fragment);});});
document.querySelectorAll('.button, .booking-back').forEach(button=>{const text=button.textContent.replace(/[→←]/g,'').trim();button.setAttribute('aria-label',button.getAttribute('aria-label')||text);button.replaceChildren();const label=document.createElement('span');label.className='button-label';label.dataset.buttonLabel='true';for(const key of ['a','b']){const row=document.createElement('span');row.className='button-label-'+key;row.dataset[key==='a'?'buttonLabelA':'buttonLabelB']='true';if(key==='b')row.setAttribute('aria-hidden','true');for(const ch of text){const char=document.createElement('span');char.className='button-motion-char';char.textContent=ch===' '?'\u00a0':ch;row.append(char);}label.append(row);}button.append(label);const icon=document.createElement('span');const left=button.id==='step-back';icon.className=left?'button-icon button-icon-left':'button-icon';icon.setAttribute('aria-hidden','true');icon.innerHTML='<svg class="button-icon-a" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.2"><path d="M4 12h15M13 6l6 6-6 6"/></svg><svg class="button-icon-b" viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.2"><path d="M4 12h15M13 6l6 6-6 6"/></svg>';if(left){icon.querySelectorAll('path').forEach(path=>path.setAttribute('d','M20 12H5M11 6l-6 6 6 6'));button.prepend(icon);}else button.append(icon);});

gsap.registerPlugin(ScrollTrigger, ScrollSmoother);

function initVideoControl(video, preference) {
 video.tabIndex=0;video.setAttribute('role','button');video.title='Click or press Space to pause or play the background video';
 let manualPause=false;
 const sync=()=>video.setAttribute('aria-label',video.paused?'Play background video':'Pause background video');
 const toggle=()=>{manualPause=!video.paused;if(video.paused)video.play().catch(sync);else video.pause();};
 video.addEventListener('click',toggle);
 video.parentElement.addEventListener('click',event=>{if(event.target!==video&&!event.target.closest('a,button,input,select'))toggle();});
 video.addEventListener('keydown',event=>{if(event.key===' '||event.key==='Enter'){event.preventDefault();toggle();}});
 video.addEventListener('play',sync);video.addEventListener('pause',sync);sync();
 const observer=new IntersectionObserver(([entry])=>{if(!entry.isIntersecting)video.pause();else if(!preference.matches&&!manualPause)video.play().catch(sync);});observer.observe(video);
 preference.addEventListener('change',()=>{if(preference.matches)video.pause();});
}

function initButtonMotion(button) {
  if (!button || matchMedia('(prefers-reduced-motion: reduce)').matches) return () => {};
  const labelA = button.querySelector('[data-button-label-a], .button-label-a');
  const labelB = button.querySelector('[data-button-label-b], .button-label-b');
  const iconA = button.querySelector('[data-button-icon-a], .button-icon-a');
  const iconB = button.querySelector('[data-button-icon-b], .button-icon-b');

  if (!labelA || !labelB) return () => {};

  const charsA = labelA.querySelectorAll('.button-motion-char');
  const charsB = labelB.querySelectorAll('.button-motion-char');

  const tl = gsap.timeline({ paused: true, defaults: { ease: 'power2.inOut' } });

  if (charsA.length && charsB.length) {
    gsap.set(charsA, { yPercent: 0 });
    gsap.set(charsB, { yPercent: 125 });
    gsap.set(labelB, { opacity: 0, visibility: 'hidden' });

    tl.to(labelB, { opacity: 1, visibility: 'visible', duration: 0.02 }, 0);
    tl.to(charsA, { yPercent: -125, duration: 0.32, stagger: 0.015 }, 0);
    tl.to(charsB, { yPercent: 0, duration: 0.32, stagger: 0.015 }, 0.03);
  } else {
    gsap.set(labelA, { yPercent: 0 });
    gsap.set(labelB, { yPercent: 125, opacity: 0, visibility: 'hidden' });

    tl.to(labelB, { opacity: 1, visibility: 'visible', duration: 0.02 }, 0);
    tl.to(labelA, { yPercent: -125, duration: 0.32 }, 0);
    tl.to(labelB, { yPercent: 0, duration: 0.32 }, 0.03);
  }

  if (iconA && iconB) {
    const isLeft = button.querySelector('.button-icon-left');
    if (isLeft) {
      gsap.set(iconA, { autoAlpha: 1, x: 0 });
      gsap.set(iconB, { autoAlpha: 0, x: 28 });
      tl.to(iconA, { autoAlpha: 0, x: -28, duration: 0.3 }, 0);
      tl.to(iconB, { autoAlpha: 1, x: 0, duration: 0.3 }, 0.04);
    } else {
      gsap.set(iconA, { autoAlpha: 1, x: 0 });
      gsap.set(iconB, { autoAlpha: 0, x: -28 });
      tl.to(iconA, { autoAlpha: 0, x: 28, duration: 0.3 }, 0);
      tl.to(iconB, { autoAlpha: 1, x: 0, duration: 0.3 }, 0.04);
    }
  }

  const enter = () => tl.play();
  const leave = () => tl.reverse();

  button.addEventListener('pointerenter', enter);
  button.addEventListener('pointerleave', leave);
  button.addEventListener('focus', enter);
  button.addEventListener('blur', leave);

  return () => {
    button.removeEventListener('pointerenter', enter);
    button.removeEventListener('pointerleave', leave);
    button.removeEventListener('focus', enter);
    button.removeEventListener('blur', leave);
    tl.kill();
    if (charsA.length && charsB.length) {
      gsap.set([...charsA, ...charsB], { clearProps: 'transform' });
    } else {
      gsap.set([labelA, labelB], { clearProps: 'transform' });
    }
    gsap.set(labelB, { clearProps: 'opacity,visibility' });
    if (iconA && iconB) {
      gsap.set([iconA, iconB], { clearProps: 'transform,opacity,visibility' });
    }
  };
}

// One context owns splits, timelines, event handlers and scroll triggers per route.
function animatePage(root) {
  const cleanup = [];
  let media;
  const context = gsap.context(() => {
    media = gsap.matchMedia();
    media.add({ motion: '(prefers-reduced-motion: no-preference)', desktop: '(min-width: 701px)' }, ({ conditions }) => {
      if (!conditions.motion) return;
      const desktop = conditions.desktop;
      // Ensure any previous smoother is cleanly killed before creating a new one
      const existingSmoother = ScrollSmoother.get();
      if (existingSmoother) {
        existingSmoother.kill();
      }

      const smoother = desktop ? ScrollSmoother.create({
        wrapper: root.closest('.smooth-wrapper'),
        content: root.parentElement,
        smooth: 0.9,
        smoothTouch: 0.1,
        effects: true,
      }) : null;

      if (smoother) {
        cleanup.push(() => smoother.kill());
      }

      const heroes = root.querySelectorAll('.hero'); heroes.forEach((section,index)=>{if(index>0){section.querySelector('.hero-content')?.classList.replace('hero-content','statement-content');}}); const hero = root.querySelector('.hero');
      const revealHeading = (element, entrance = false) => {
        const words = element.querySelectorAll('.split-word');
        if (words.length) {
          gsap.from(words, {
            yPercent: 110,
            opacity: 1,
            duration: 0.85,
            stagger: 0.05,
            ease: 'power3.out',
            delay: entrance ? 0.18 : 0.05,
            ...(entrance ? {} : {
              scrollTrigger: {
                trigger: element,
                start: 'top 88%',
                toggleActions: 'play none none none',
                once: true,
              }
            }),
          });
        } else {
          gsap.from(element, {
            y: 40,
            opacity: 1,
            duration: 0.85,
            ease: 'power3.out',
            delay: entrance ? 0.18 : 0.05,
            ...(entrance ? {} : {
              scrollTrigger: {
                trigger: element,
                start: 'top 88%',
                toggleActions: 'play none none none',
                once: true,
              }
            }),
          });
        }
      };

      root.querySelectorAll('.intro h2,.mosaic-copy h2,.statement h2,.venue-promotions h2,.about-teaser h2,.final-cta h2,.contact-gallery h2')
        .forEach(element => revealHeading(element));

      if (hero) {
        const heroBottom = hero.querySelector('.hero-bottom');
        const heroWords = hero.querySelectorAll('.hero-content .split-word');
        const heroButton = hero.querySelector('.hero-content .button');
        const heroTl = gsap.timeline({ defaults: { ease: 'power3.out' }, onComplete: () => finishHeroIntro(hero.querySelector('video')) })
          .from('.hero-image', { opacity: 0, duration: 1 }, 0)
          .from('.hero-content .eyebrow', { y: 20, opacity: 0, duration: 0.7 }, 0.15);

        if (heroWords.length) {
          heroTl.from(heroWords, { yPercent: 115, opacity: 0, duration: 0.9, stagger: 0.035, ease: 'power3.out' }, 0.28);
        } else {
          const heroH1 = hero.querySelector('h1');
          if (heroH1) {
            heroTl.from(heroH1, { y: 35, opacity: 0, duration: 0.85 }, 0.28);
          }
        }

        if (heroButton) {
          heroTl.from(heroButton, { y: 20, opacity: 0, duration: 0.7 }, 0.65);
        }
        if (heroBottom) {
          heroTl.from(heroBottom, { opacity: 0, duration: 0.6 }, 0.9);
        }

        // Inner image parallax
        gsap.to('.hero-image', {
          yPercent: 15,
          ease: 'none',
          scrollTrigger: { trigger: hero, start: 'top top', end: 'bottom top', scrub: 0.2 },
        });

        // Bottom scroll indicator fades out quickly (if present)
        if (heroBottom) {
          gsap.to(heroBottom, {
            opacity: 0,
            ease: 'none',
            scrollTrigger: { trigger: hero, start: 'top top', end: '25% top', scrub: 0.15 },
          });
        }

        // Hero container scale down on scroll (exact Spyker Cars behavior, crisp square edges)
        gsap.fromTo(hero, { scale: 1 }, {
          scale: 0.8,
          ease: 'none',
          transformOrigin: 'center center',
          scrollTrigger: {
            trigger: hero,
            start: '30% top',
            end: 'bottom top',
            scrub: 0.15,
          },
        });
      }

      root.querySelectorAll('.intro,.about-teaser,.statement').forEach(section => {
        gsap.from(section.querySelectorAll('p,.button'), {
          opacity: 1,
          duration: 0.8,
          stagger: 0.12,
          ease: 'power2.out',
          scrollTrigger: { trigger: section, start: 'top 78%', once: true },
        });
      });

      // Editorial figures: animate scale in ONCE on scroll (no scroll out, matching user specification, no border-radius)
      root.querySelectorAll('.editorial figure').forEach((fig, index) => {
        const frame = fig.querySelector('.photo-frame');
        const image = frame?.querySelector('img');
        if (!frame || !image) return;

        // Animate in on scroll, and ONLY ONCE: scale 0.8 to 1 with zero border radius
        gsap.fromTo(frame,
          { scale: 0.82, opacity: 0 },
          {
            scale: 1, opacity: 1,
            duration: 1.1,
            ease: 'power3.out',
            scrollTrigger: {
              trigger: fig,
              start: 'top 85%',
              once: true,
            }
          }
        );

        // Keep parallax effect inside the image matching scrub speed - increased parallax depth
        gsap.fromTo(image,
          { yPercent: -18, },
          {
            yPercent: 18,
            ease: 'none',
            scrollTrigger: {
              trigger: fig,
              start: 'top bottom',
              end: 'bottom top',
              scrub: 0.2,
            }
          }
        );

        // Desktop vertical staggered float - increased parallax travel
        if (desktop) {
          gsap.fromTo(fig, { y: index % 2 ? 65 : 95 }, {
            y: index % 2 ? -65 : -95,
            ease: 'none',
            scrollTrigger: { trigger: fig, start: 'top bottom', end: 'bottom top', scrub: 0.8 },
          });
        }
      });

      // Statement Section: scroll scale from small scale to normal full width, and scale down smoothly on scroll out (no sticky)
      const statement = root.querySelector('.statement .scroll-scale-frame');
      if (statement) {
        const statementTl = gsap.timeline({
          scrollTrigger: {
            trigger: statement,
            start: 'top bottom',
            end: 'bottom top',
            scrub: 0.5,
          }
        });

        statementTl
          .fromTo(statement,
            { transformOrigin: 'center center' },
            { ease: 'sine.out', duration: 1 }
          )
          .to(statement, {
            duration: 0.5,
          })
          .to(statement, {
            ease: 'sine.in',
            duration: 1,
          });

        const statementMedia = statement.querySelectorAll('.scroll-scale-img, .background-video');
        if (statementMedia.length) {
          gsap.fromTo(statementMedia,
            { yPercent: -5, },
            {
              yPercent: 5,
              ease: 'none',
              scrollTrigger: {
                trigger: statement,
                start: 'top bottom',
                end: 'bottom top',
                scrub: 0.5,
              }
            }
          );
        }
      }

      // Venue promotions: animate images similar to gallery images, with stagger
      const venuePromos = root.querySelector('.venue-promotions');
      const promoGrid = root.querySelector('.promotion-grid');
      if (venuePromos && promoGrid) {
        const headerItems = venuePromos.querySelectorAll('.eyebrow, .promotion-note');
        if (headerItems.length) {
          gsap.from(headerItems, {
            y: 20,
            opacity: 1,
            duration: 0.8,
            stagger: 0.1,
            ease: 'power3.out',
            scrollTrigger: {
              trigger: venuePromos,
              start: 'top 85%',
              once: true,
            }
          });
        }

        const figures = Array.from(promoGrid.querySelectorAll('figure'));
        if (figures.length) {
          // Staggered entrance matching gallery image animation (from opacity: 0 to opacity: 1)
          gsap.fromTo(figures.map(figure => figure.querySelector("img")),
            { opacity: 0 },
            {
              opacity: 1,
              duration: 0.9,
              stagger: 0.16,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: promoGrid,
                start: 'top 88%',
                once: true,
              }
            }
          );

          // Staggered captions matching gallery info
          const captions = Array.from(promoGrid.querySelectorAll('figcaption'));
          if (captions.length) {
            gsap.from(captions, {
              y: 18,
              opacity: 1,
              duration: 0.85,
              stagger: 0.16,
              delay: 0.12,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: promoGrid,
                start: 'top 88%',
                once: true,
              }
            });
          }

          // Subtle parallax on the images inside matching gallery
          figures.forEach((fig) => {
            const img = fig.querySelector('img');
            if (img) {
              gsap.fromTo(img,
                { yPercent: -3, },
                {
                  yPercent: 3,
                  ease: 'none',
                  scrollTrigger: {
                    trigger: fig,
                    start: 'top bottom',
                    end: 'bottom top',
                    scrub: 0.25,
                  }
                }
              );
            }
          });
        }
      }

      // Heritage Mosaic Teaser on Home page (matching Spyker Cars floating scattered images, crisp edges)
      const mosaic = root.querySelector('.about-teaser-mosaic');
      if (mosaic) {
        const items = Array.from(mosaic.querySelectorAll('.mosaic-item'));
        if (items.length) {
          // 1. Entrance animation: animate in smoothly once, preserving CSS class opacity hierarchy
          items.forEach((item, idx) => {
            const targetOpacity = parseFloat(window.getComputedStyle(item).opacity) || 1;
            gsap.fromTo(item,
              { scale: 0.82, opacity: 0, y: 32 },
              {
                opacity: targetOpacity,
                scale: 1, y: 0,
                duration: 1.1,
                delay: (idx % 3) * 0.08,
                ease: 'power3.out',
                clearProps: 'opacity',
                scrollTrigger: {
                  trigger: mosaic,
                  start: 'top 85%',
                  once: true,
                }
              }
            );
          });

          // 2. Vertical scroll parallax on .mosaic-inner (restrained subtle travel on smaller images)
          items.forEach((item, idx) => {
            const inner = item.querySelector('.mosaic-inner') || item;
            const isSmall = item.classList.contains('mosaic-img-top-left') ||
                            item.classList.contains('mosaic-img-mid-right') ||
                            item.classList.contains('mosaic-img-bot-left');
            const maxTravel = isSmall ? 24 : 44;
            const yOffset = (idx % 2 === 0 ? -1 : 1) * maxTravel;
            gsap.fromTo(inner,
              { y: yOffset },
              {
                y: -yOffset,
                ease: 'none',
                scrollTrigger: {
                  trigger: mosaic,
                  start: 'top bottom',
                  end: 'bottom top',
                  scrub: 0.35,
                }
              }
            );
          });

          // 3. Gentle cursor follow on inner images (throttled with rAF for 60fps performance)
          let ticking = false;
          let mouseX = 0;
          let mouseY = 0;
          const onMouseMove = (e) => {
            const rect = mosaic.getBoundingClientRect();
            mouseX = (e.clientX - rect.left - rect.width / 2) / (rect.width / 2);
            mouseY = (e.clientY - rect.top - rect.height / 2) / (rect.height / 2);

            if (!ticking) {
              ticking = true;
              requestAnimationFrame(() => {
                ticking = false;
                items.forEach((item, idx) => {
                  const img = item.querySelector('img') || item;
                  const depth = ((idx % 3) + 1) * 9;
                  gsap.to(img, {
                    x: mouseX * depth,
                    y: mouseY * depth,
                    duration: 1.2,
                    ease: 'power2.out',
                    overwrite: 'auto'
                  });
                });
              });
            }
          };

          if (window.matchMedia('(hover: hover) and (pointer: fine)').matches) {
            const resetCursor = () => items.forEach(item => gsap.to(item.querySelector('img'), { x: 0, y: 0, duration: 1.2, ease: 'power2.out', overwrite: 'auto' }));
            mosaic.addEventListener('mousemove', onMouseMove, { passive: true });
            mosaic.addEventListener('mouseleave', resetCursor);
            cleanup.push(() => {
              mosaic.removeEventListener('mousemove', onMouseMove);
              mosaic.removeEventListener('mouseleave', resetCursor);
              items.forEach(item => gsap.set(item.querySelector('img'), { clearProps: 'transform' }));
            });
          }
        }
      }

      // About Page: Horizontal scroll timeline on desktop, vertical scroll animation on mobile
      const heritagePage = root.querySelector('.spyker-heritage-page');
      const heritageContainer = heritagePage?.querySelector('.heritage-horizontal-container');
      const heritageTrack = heritageContainer?.querySelector('.heritage-scroll-track');
      if (heritagePage && heritageContainer && heritageTrack) {
        const textFrames = Array.from(heritageTrack.querySelectorAll('.heritage-text-frame'));
        const imageFrames = Array.from(heritageTrack.querySelectorAll('.heritage-image-frame'));

        if (desktop) {
          // --- DESKTOP: Horizontal Scroll with Pinning ---
          const getDistance = () => Math.max(0, heritageTrack.scrollWidth - heritageContainer.clientWidth);

          const horizontalTl = gsap.timeline({
            scrollTrigger: {
              trigger: heritageContainer,
              start: 'top top',
              end: () => `+=${getDistance()}`,
              pin: true,
              scrub: 1,
              anticipatePin: 1,
              invalidateOnRefresh: true,
            }
          });

          // 1. Horizontal track travel
          horizontalTl.to(heritageTrack, {
            x: () => -getDistance(),
            ease: 'none',
            duration: 1,
          }, 0);

          const totalDistance = Math.max(1, getDistance());
          const enterTravelProgress = Math.min(0.26, Math.max(0.12, (window.innerWidth * 0.52) / totalDistance));

          const getFrameLeft = (frame) => {
            const trackRect = heritageTrack.getBoundingClientRect();
            const frameRect = frame.getBoundingClientRect();
            return frameRect.left - trackRect.left;
          };

          textFrames.forEach((frame, idx) => {

            const frameLeft = getFrameLeft(frame);
            const isInitial = frameLeft < window.innerWidth * 0.75;
            const words = frame.querySelectorAll('.split-word');
            const tag = frame.querySelector('.heritage-chapter-tag');
            const title = frame.querySelector('.heritage-milestone-title');
            const copy = frame.querySelector('.heritage-milestone-copy');
            const year = frame.querySelector('.heritage-giant-year');

            if (isInitial) {
              const loadDelay = 0.12 + idx * 0.18;
              if (tag) gsap.from(tag, { y: 15, opacity: 1, duration: 0.65, ease: 'power3.out', delay: loadDelay });
              if (words.length) {
                gsap.from(words, { yPercent: 110, opacity: 1, duration: 0.85, stagger: 0.03, ease: 'power3.out', delay: loadDelay + 0.05 });
              } else if (title) {
                gsap.from(title, { y: 35, opacity: 0, duration: 0.85, ease: 'power3.out', delay: loadDelay + 0.05 });
              }
              if (copy) gsap.from(copy, { y: 25, opacity: 1, duration: 0.85, ease: 'power3.out', delay: loadDelay + 0.1 });
              if (year) gsap.from(year, { opacity: 1, duration: 0.95, ease: 'power3.out', delay: loadDelay + 0.15 });
            } else if (totalDistance > 0) {
              const enterTime = Math.max(0, Math.min(0.96, (frameLeft - window.innerWidth * 0.98) / totalDistance));

              if (tag) {
                horizontalTl.fromTo(tag,
                  { y: 15, opacity: 1 },
                  { y: 0, opacity: 1, ease: 'sine.inOut', duration: enterTravelProgress },
                  enterTime
                );
              }
              if (words.length) {
                horizontalTl.fromTo(words,
                  { yPercent: 90, opacity: 1 },
                  { yPercent: 0, opacity: 1, stagger: 0.012, ease: 'sine.inOut', duration: enterTravelProgress },
                  enterTime + 0.005
                );
              } else if (title) {
                horizontalTl.fromTo(title,
                  { y: 30, opacity: 1 },
                  { y: 0, opacity: 1, ease: 'sine.inOut', duration: enterTravelProgress },
                  enterTime + 0.005
                );
              }
              if (copy) {
                horizontalTl.fromTo(copy,
                  { y: 20, opacity: 1 },
                  { y: 0, opacity: 1, ease: 'sine.inOut', duration: enterTravelProgress },
                  enterTime + 0.015
                );
              }
              if (year) {
                horizontalTl.fromTo(year,
                  { opacity: 0 },
                  { opacity: 1, ease: 'sine.inOut', duration: enterTravelProgress * 1.05 },
                  enterTime + 0.02
                );
              }
            }
          });

          const ending = heritageTrack.querySelector('.heritage-end-frame');
          if (ending && totalDistance > 0) {
            const at = Math.max(0, Math.min(.96, (getFrameLeft(ending) - window.innerWidth * .98) / totalDistance));
            horizontalTl.fromTo(ending.querySelector('.button'), {y:20}, {y:0, duration:enterTravelProgress, ease:'sine.inOut'}, at);
          }

          imageFrames.forEach((frame, idx) => {
            const frameLeft = getFrameLeft(frame);
            const isInitial = frameLeft < window.innerWidth * 0.75;
            const img = frame.querySelector('.heritage-parallax-img');
            const parallaxStart = Math.max(0, (frameLeft - window.innerWidth) / totalDistance);
            const parallaxEnd = Math.min(1, (frameLeft + frame.offsetWidth) / totalDistance);
            const parallaxDuration = Math.max(0.08, parallaxEnd - parallaxStart);

            // Counter-parallax on image frame
            if (img && totalDistance > 0) {
              horizontalTl.fromTo(img,
                { xPercent: 12, },
                { xPercent: -12, ease: 'none', duration: parallaxDuration },
                parallaxStart
              );
            }

            if (isInitial) {
              gsap.fromTo(frame,
                { scale: 0.82, opacity: 0 },
                {
                  scale: 1, opacity: 1,
                  duration: 1.2,
                  ease: 'power3.out',
                  delay: 0.22 + idx * 0.18,
                  transformOrigin: 'center center',
                }
              );
            } else if (totalDistance > 0) {
              const enterTime = Math.max(0, Math.min(0.96, (frameLeft - window.innerWidth * 0.98) / totalDistance));

              horizontalTl.fromTo(frame,
                { scale: 0.82, opacity: 0 },
                {
                  scale: 1, opacity: 1,
                  ease: 'sine.inOut',
                  duration: enterTravelProgress,
                  transformOrigin: 'center center',
                },
                enterTime
              );
            }
          });

        } else {
          // --- MOBILE: Rich Vertical Scroll Animations ---
          // 1. Archival photos: scale from small to full size with scrub (matching hero scale animation)
          imageFrames.forEach((frame) => {
            gsap.fromTo(frame,
              { scale: 0.82, opacity: 0.25 },
              {
                scale: 1, opacity: 1,
                ease: 'power2.out',
                scrollTrigger: {
                  trigger: frame,
                  start: 'top 92%',
                  end: 'top 42%',
                  scrub: 1,
                }
              }
            );

            const img = frame.querySelector('.heritage-parallax-img');
            if (img) {
              gsap.fromTo(img,
                { yPercent: -10, },
                {
                  yPercent: 10,
                  ease: 'none',
                  scrollTrigger: {
                    trigger: frame,
                    start: 'top bottom',
                    end: 'bottom top',
                    scrub: 1,
                  }
                }
              );
            }
          });

          // 2. Text frames: Word stagger, copy fade, and massive year scaling entrance
          textFrames.forEach((frame) => {
            const words = frame.querySelectorAll('.split-word');
            const title = frame.querySelector('.heritage-milestone-title');
            const copy = frame.querySelector('.heritage-milestone-copy');
            const year = frame.querySelector('.heritage-giant-year');
            const btn = frame.querySelector('.button');

            const textAnimTargets = words.length ? words : (title ? [title] : []);
            if (textAnimTargets.length) {
              gsap.from(textAnimTargets, {
                y: 30,
                opacity: 1,
                duration: 0.75,
                stagger: 0.02,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: frame,
                  start: 'top 85%',
                  toggleActions: 'play none none none',
                }
              });
            }

            if (copy) {
              gsap.from(copy, {
                y: 20,
                opacity: 1,
                duration: 0.7,
                delay: 0.1,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: frame,
                  start: 'top 85%',
                  toggleActions: 'play none none none',
                }
              });
            }

            if (year) {
              gsap.from(year, {
                opacity: 1,
                duration: 0.85,
                delay: 0.15,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: frame,
                  start: 'top 85%',
                  toggleActions: 'play none none none',
                }
              });
            }

            if (btn) {
              gsap.from(btn, {
                y: 20,
                opacity: 1,
                duration: 0.7,
                delay: 0.2,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: frame,
                  start: 'top 85%',
                  toggleActions: 'play none none none',
                }
              });
            }
          });
        }

        // Force a refresh once layout/fonts settle
        requestAnimationFrame(() => ScrollTrigger.refresh());
      }

      // Other photo frames outside editorial and statement
      root.querySelectorAll('.photo-frame:not(.editorial .photo-frame):not(.scroll-scale-frame):not(.mosaic-item .photo-frame)').forEach((frame) => {
        const image = frame.querySelector('img');
        gsap.from(frame, {
          scale: 0.82,
          clipPath: 'inset(12% 0% 12% 0%)',
          opacity: 0,
          duration: 1.1,
          ease: 'power3.out',
          scrollTrigger: { trigger: frame, start: 'top 92%', once: true },
        });
        if (image) {
          gsap.fromTo(image, { yPercent: -7, }, {
            yPercent: 7,
            ease: 'none',
            scrollTrigger: { trigger: frame, start: 'top bottom', end: 'bottom top', scrub: 0.8 },
          });
        }
      });

      root.querySelectorAll('.editorial figcaption').forEach(caption => {
        gsap.from(caption, {
          opacity: 1,
          duration: 0.65,
          scrollTrigger: { trigger: caption, start: 'top 92%', once: true },
        });
      });

      // Contact Page: load animation for form, details, and scroll trigger for gallery
      const contactPage = root.querySelector('.contact-page');
      if (contactPage) {
        const isMenuTransition = document.body.classList.contains('menu-transitioning') || !!document.querySelector('.menu-panel');
        const entranceDelay = isMenuTransition ? 0.38 : 0.08;
        const leftCol = contactPage.querySelector('div:first-child');
        const rightCol = contactPage.querySelector('div:last-child');

        if (leftCol) {
          gsap.from(leftCol.querySelectorAll('.contact-header-block > *, .contact-detail-card, .contact-map-card'), {
            y: 26,
            opacity: 0,
            duration: 0.85,
            stagger: 0.08,
            ease: 'power3.out',
            delay: entranceDelay,
          });
        }

        if (rightCol) {
          const formHeading = rightCol.querySelector('h2');
          const fields = rightCol.querySelectorAll('.field');
          const submitBtn = rightCol.querySelector('.button');
          const reserveSection = rightCol.querySelector('.contact-reserve');

          const rightTl = gsap.timeline({ defaults: { ease: 'power3.out' }, delay: entranceDelay + 0.06 });
          if (formHeading) {
            rightTl.from(formHeading, { y: 22, opacity: 0, duration: 0.75 }, 0);
          }
          if (fields.length) {
            rightTl.from(fields, { y: 18, opacity: 0, duration: 0.65, stagger: 0.07 }, 0.1);
          }
          if (submitBtn) {
            rightTl.from(submitBtn, { y: 16, opacity: 0, duration: 0.6 }, 0.3);
          }
          if (reserveSection) {
            rightTl.from(reserveSection.querySelectorAll('h2, a'), {
              y: 18,
              opacity: 0,
              duration: 0.65,
              stagger: 0.08,
            }, 0.38);
          }
        }

        const contactGallery = root.querySelector('.contact-gallery');
        if (contactGallery) {
          const galleryH2 = contactGallery.querySelector('h2');
          const galleryPhotos = contactGallery.querySelectorAll('img, .photo-frame');
          const galleryNote = contactGallery.querySelector('.small');

          if (galleryH2) {
            gsap.from(galleryH2, {
              y: 28,
              opacity: 1,
              duration: 0.85,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: contactGallery,
                start: 'top 86%',
                once: true,
              }
            });
          }

          if (galleryPhotos.length) {
            gsap.from(galleryPhotos, {
              opacity: 0,
              duration: 0.95,
              stagger: 0.1,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: contactGallery,
                start: 'top 84%',
                once: true,
              }
            });
          }

          if (galleryNote) {
            gsap.from(galleryNote, {
              opacity: 1,
              duration: 0.7,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: contactGallery,
                start: 'top 78%',
                once: true,
              }
            });
          }
        }
      }

      // My Reservations: load animation for header/lookup and reservations items
      const myReservations = root.querySelector('.my-reservations-page');
      if (myReservations) {
        const isMenuTransition = document.body.classList.contains('menu-transitioning') || !!document.querySelector('.menu-panel');
        const entranceDelay = isMenuTransition ? 0.38 : 0.08;

        const titleElements = myReservations.querySelectorAll('.reservation-title .eyebrow, .reservation-title h1, .reservation-title p');
        if (titleElements.length) {
          gsap.from(titleElements, {
            y: 24,
            opacity: 0,
            duration: 0.8,
            stagger: 0.08,
            ease: 'power3.out',
            delay: entranceDelay,
            clearProps: 'transform,opacity',
          });
        }

        const lookupBar = myReservations.querySelector('.lookup-bar');
        if (lookupBar) {
          gsap.from(lookupBar, {
            y: 20,
            opacity: 0,
            duration: 0.75,
            ease: 'power3.out',
            delay: entranceDelay + 0.12,
            clearProps: 'transform,opacity',
          });
        }

        const historyRows = Array.from(myReservations.querySelectorAll('.history-row'));
        if (historyRows.length) {
          const initialRows = [];
          const offscreenRows = [];
          const windowHeight = window.innerHeight;

          historyRows.forEach(row => {
            const rect = row.getBoundingClientRect();
            if (rect.top < windowHeight * 0.95) {
              initialRows.push(row);
            } else {
              offscreenRows.push(row);
            }
          });

          if (initialRows.length) {
            gsap.from(initialRows, {
              y: 26,
              opacity: 0,
              duration: 0.8,
              stagger: 0.08,
              ease: 'power3.out',
              delay: entranceDelay + 0.22,
              clearProps: 'transform,opacity',
            });
          }

          if (offscreenRows.length) {
            gsap.from(offscreenRows, {
              y: 26,
              opacity: 1,
              duration: 0.8,
              stagger: 0.08,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: offscreenRows[0],
                start: 'top 90%',
                once: true,
              },
              clearProps: 'transform,opacity',
            });
          }
        }

        const emptyNotice = myReservations.querySelector('.empty');
        if (emptyNotice) {
          gsap.from(emptyNotice, {
            y: 20,
            opacity: 0,
            duration: 0.75,
            ease: 'power3.out',
            delay: entranceDelay + 0.22,
            clearProps: 'transform,opacity',
          });
        }
      } else {
        // Fallback for regular appointment title if not my-reservations
        const generalResTitle = root.querySelector('.reservation-title');
        if (generalResTitle) {
          gsap.from(generalResTitle, {
            opacity: 0,
            y: 12,
            duration: 0.65,
            ease: 'power3.out',
          });
        }

        // Reservation Page: sticky sidebar pinning on desktop (for ScrollSmoother compatibility)
        const bookingLayout = root.querySelector('.booking-layout');
        const sidebarSticky = root.querySelector('.sidebar-sticky');
        if (desktop && bookingLayout && sidebarSticky) {
          ScrollTrigger.create({
            trigger: sidebarSticky,
            start: 'top 50px',
            end: () => `+=${Math.max(0, bookingLayout.offsetHeight - sidebarSticky.offsetHeight)}`,
            pin: true,
            pinSpacing: false,
            invalidateOnRefresh: true,
          });
        }
      }

      // Gallery Page: sticky left component + home images scale animation + reduced parallax
      const newsPage = root.querySelector('.spyker-news-page');
      if (newsPage) {
        // Sticky left column pinning on desktop (matching spykercars.com/news)
        if (desktop) {
          const sections = newsPage.querySelectorAll('.news-editorial-section');
          sections.forEach(section => {
            const grid = section.querySelector('.news-editorial-grid');
            const stickyCol = section.querySelector('.news-sticky-col');
            if (grid && stickyCol) {
              ScrollTrigger.create({
                trigger: stickyCol,
                start: 'top top',
                endTrigger: grid,
                end: () => `bottom ${stickyCol.offsetHeight}px`,
                pin: true,
                pinSpacing: false,
                invalidateOnRefresh: true,
              });
            }
          });
        }

        const newsCards = newsPage.querySelectorAll('.news-card, .news-featured-banner');
        newsCards.forEach((card) => {
          const frame = card.querySelector('.news-frame, .featured-banner-frame');
          const img = card.querySelector('.news-inner img, .featured-banner-inner img');
          const info = card.querySelector('.news-card-info, .featured-banner-content');

          // Animate like home images: gets to default size () from small scale ()
          if (frame) {
            gsap.fromTo(frame,
              { scale: 0.82, opacity: 0 },
              {
                scale: 1, opacity: 1,
                duration: 1.05,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: card,
                  start: 'top 88%',
                  once: true,
                }
              }
            );
          } else {
            gsap.fromTo(card,
              { scale: 0.82, opacity: 0 },
              {
                scale: 1, opacity: 1,
                duration: 1.0,
                ease: 'power3.out',
                scrollTrigger: {
                  trigger: card,
                  start: 'top 88%',
                  once: true,
                }
              }
            );
          }

          if (info) {
            gsap.from(info, {
              y: 20,
              opacity: 1,
              duration: 0.85,
              ease: 'power3.out',
              scrollTrigger: {
                trigger: card,
                start: 'top 88%',
                once: true,
              }
            });
          }

          // Subtle parallax: reduced from -14/14% to -3/3% so images stay aligned to frames and each other
          if (img) {
            gsap.fromTo(img,
              { yPercent: -3, },
              {
                yPercent: 3,
                ease: 'none',
                scrollTrigger: {
                  trigger: card,
                  start: 'top bottom',
                  end: 'bottom top',
                  scrub: 0.3,
                }
              }
            );
          }
        });
      }

      // Initialize character roll & arrow slide motion for all buttons and header actions
      const allButtons = [
        ...root.querySelectorAll('.button, .header-action, .booking-back'),
        ...document.querySelectorAll('.header .button, .header .header-action')
      ];
      const uniqueButtons = Array.from(new Set(allButtons));
      uniqueButtons.forEach(button => {
        const dispose = initButtonMotion(button);
        cleanup.push(dispose);
      });

      return () => {
        cleanup.splice(0).forEach(dispose => {
          try { dispose(); } catch {}
        });
        try { smoother?.kill(); } catch {}
      };
    });
  }, root);
  return () => {
    cleanup.forEach(dispose => {
      try { dispose(); } catch {}
    });
    try {
      ScrollTrigger.getAll().forEach(st => {
        if (st.trigger && (root.contains(st.trigger) || st.trigger === root || st.vars?.pin)) {
          st.kill(false);
        }
      });
    } catch {}
    try { media?.revert(); } catch {}
    try { context.revert(); } catch {}
  };
}
document.querySelectorAll('.spyker-footer-nav a,.spyker-footer-right a').forEach(link=>link.classList.add('prototype-link'));let dispose;document.fonts.ready.then(()=>{dispose=animatePage(root);ScrollTrigger.refresh();document.dispatchEvent(new Event('holeinhub:motion-ready'));});root.querySelectorAll('video').forEach(video=>initVideoControl(video,reduced));document.fonts.ready.then(()=>ScrollTrigger.refresh());window.addEventListener('pageshow',()=>ScrollTrigger.refresh());})();
