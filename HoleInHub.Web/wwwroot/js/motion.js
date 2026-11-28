(()=>{if(!window.gsap||!window.ScrollTrigger||!window.ScrollSmoother)return;const root=document.querySelector('main');const reduced=matchMedia('(prefers-reduced-motion: reduce)');const finishHeroIntro=v=>{if(v&&!reduced.matches)v.play().catch(()=>{});};
const aliases={'.editorial-copy':'intro','.program-section':'venue-promotions','.program-posters':'promotion-grid','.program-mosaic':'about-teaser-mosaic','.mosaic-picture':'mosaic-item','.heritage-page':'spyker-heritage-page','.heritage-track':'heritage-scroll-track','.heritage-copy':'heritage-text-frame','.heritage-copy h2':'heritage-milestone-title','.heritage-copy p':'heritage-milestone-copy','.heritage-copy strong':'heritage-giant-year','.heritage-copy > span':'heritage-chapter-tag','.heritage-end':'heritage-end-frame','.hero:first-child > video':'hero-image','.feature-grid':'editorial'};Object.entries(aliases).forEach(([selector,name])=>root.querySelectorAll(selector).forEach(el=>el.classList.add(name)));
const statement=root.querySelectorAll('.hero')[1];if(statement){statement.classList.add('statement');const frame=document.createElement('div');frame.className='scroll-scale-frame';while(statement.firstChild)frame.append(statement.firstChild);statement.append(frame);frame.querySelector('video')?.classList.add('background-video');}
const contact=root.querySelector('.contact-grid');if(contact){gsap.matchMedia().add('(prefers-reduced-motion: no-preference)',()=>{gsap.from(contact.querySelectorAll('section'),{y:26,opacity:0,duration:.85,stagger:.08,ease:'power3.out',delay:.08});});}
const reservation=root.querySelector('#reservation-form');if(reservation){const title=root.querySelector('h1');title?.classList.add('reservation-title');}
const heritage=root.querySelector('.heritage-page');if(heritage){const container=document.createElement('div');container.className='heritage-horizontal-container';const track=heritage.querySelector('.heritage-track');track.before(container);container.append(track);track.querySelectorAll('.heritage-group > img').forEach(img=>{const frame=document.createElement('div');frame.className='heritage-image-frame';img.before(frame);frame.append(img);img.classList.add('heritage-parallax-img');});}
root.querySelectorAll('.feature-grid article').forEach(article=>{const fig=document.createElement('figure');article.replaceWith(fig);fig.append(article);const img=article.querySelector('img'),frame=document.createElement('div');frame.className='photo-frame';img.before(frame);frame.append(img);});
root.querySelectorAll('h1,h2').forEach(h=>{const walk=document.createTreeWalker(h,NodeFilter.SHOW_TEXT),texts=[];while(walk.nextNode())texts.push(walk.currentNode);texts.forEach(text=>{const fragment=document.createDocumentFragment();text.textContent.split(/(\s+)/).forEach(word=>{if(!word.trim()){fragment.append(document.createTextNode(word));return;}const wrap=document.createElement('span'),inner=document.createElement('span');wrap.className='split-word-wrap';inner.className='split-word';inner.textContent=word;wrap.append(inner);fragment.append(wrap);});text.replaceWith(fragment);});});

function initBackgroundVideo(video, preference) {
  if (video.controls) return () => {};
  video.removeAttribute('role');
  video.removeAttribute('tabindex');
  video.removeAttribute('title');
  video.setAttribute('aria-hidden', 'true');
  let isVisible = false;
  const syncPlayback = () => {
    if (!isVisible || preference.matches) video.pause();
    else video.play().catch(() => {});
  };
  const preferenceChange = () => syncPlayback();
  preference.addEventListener('change', preferenceChange);
  const observer = new IntersectionObserver(([entry]) => {
    isVisible = entry.isIntersecting;
    syncPlayback();
  }, { threshold: 0.01 });
  observer.observe(video);
  return () => {
    observer.disconnect();
    preference.removeEventListener('change', preferenceChange);
  };
}

// One context owns splits, timelines, event handlers and scroll triggers per route.
function animatePage(root) {
  const cleanup = [];
  let media;
  const context = gsap.context(() => {
    media = gsap.matchMedia();
    media.add({ motion: '(prefers-reduced-motion: no-preference)', desktop: '(min-width: 701px)', wideDesktop: '(min-width: 901px)' }, ({ conditions }) => {
      if (!conditions.motion) {
        return;
      }
      const desktop = conditions.desktop;
      const galleryDesktop = conditions.wideDesktop;
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

      root.querySelectorAll('[data-venue-title]').forEach(element => revealHeading(element, true));

      root.querySelectorAll('.venue-contact > section').forEach(section => {
        const entrance = gsap.timeline({
          defaults: { ease: 'none' },
          scrollTrigger: { trigger: section, start: 'top 90%', end: 'top 75%', scrub: .25 },
        })
          .from(section.querySelectorAll('h2 .split-word'), { yPercent: 110, duration: .85, stagger: .05 }, 0)
          .from(section.querySelectorAll('dl > div, p, address, .venue-icon, .button, .venue-social-link'), {
            y: 20, opacity: 0, duration: .65, stagger: .08,
          }, .2);
        const finish = () => { entrance.scrollTrigger.disable(false); entrance.progress(1).pause(); };
        const resume = event => {
          if (!section.contains(event.relatedTarget)) { entrance.scrollTrigger.enable(false, false); entrance.scrollTrigger.update(); }
        };
        section.addEventListener('focusin', finish);
        section.addEventListener('focusout', resume);
        cleanup.push(() => { section.removeEventListener('focusin', finish); section.removeEventListener('focusout', resume); });
      });

      const clubhouse = root.querySelector('.venue-location-content');
      if (clubhouse && conditions.wideDesktop) {
        // ScrollSmoother transforms the page, so native sticky needs a scroll pin here.
        gsap.set(clubhouse, { position: 'relative', top: 0 });
        ScrollTrigger.create({
          trigger: clubhouse,
          start: 'top 124px',
          end: () => '+=' + Math.max(0, clubhouse.closest('.venue-contact').querySelector('.venue-reach').offsetTop - clubhouse.closest('.venue-location').offsetTop),
          pin: true,
          pinSpacing: false,
          invalidateOnRefresh: true,
        });
      }

      root.querySelectorAll('.venue-next h2').forEach(heading => {
        gsap.timeline({
          defaults: { ease: 'none' },
          scrollTrigger: { trigger: heading, start: 'top 90%', end: 'top 45%', scrub: .8 },
        })
          .from(heading.querySelectorAll('.split-word'), { yPercent: 110, duration: 1, stagger: .08 })
          .from(heading.parentElement.querySelector('.button'), { y: 24, opacity: 0, duration: .7 }, .35);
      });

      root.querySelectorAll('.venue-contact-heading').forEach(heading => {
        gsap.from(heading.querySelector('.venue-contact-image'), { opacity: 0, duration: 1, ease: 'power3.out' });
        gsap.from(heading.querySelector('.venue-contact-copy p'), { y: 20, opacity: 0, duration: .7, delay: .5, ease: 'power3.out' });
        gsap.fromTo(heading, { scale: 1 }, {
          scale: desktop ? .94 : .98,
          ease: 'none',
          scrollTrigger: { trigger: heading, start: 'top top', end: 'bottom top', scrub: .2 },
        });
        gsap.fromTo(heading.querySelector('.venue-contact-image'), { scale: 1.14, yPercent: -4 }, {
          scale: 1.08,
          yPercent: 4,
          ease: 'none',
          scrollTrigger: { trigger: heading, start: 'top bottom', end: 'bottom top', scrub: .2 },
        });
      });

      root.querySelectorAll('.intro h2:not(#home-experience-heading),.mosaic-copy h2,.statement h2,.venue-promotions h2,.about-teaser h2,.final-cta h2,.contact-gallery h2')
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
      root.querySelectorAll('.editorial:not(.homepage-editorial) figure').forEach((fig, index) => {
        const frame = fig.querySelector('.photo-frame');
        const image = frame?.querySelector('img');
        if (!frame || !image || frame.hasAttribute('data-parallax') || frame.hasAttribute('data-motion')) return;

        window.HoleInHubMotion.reveal(frame, { trigger: fig, start: 'top 88%', scale: .96, y: 0, duration: 0.65 });

        // Keep parallax effect inside the image matching scrub speed - increased parallax depth
        window.HoleInHubMotion.parallax(image, { trigger: fig, depth: 5, scrub: 0.45 });

        // Desktop vertical staggered float - increased parallax travel
        if (desktop) {
          gsap.fromTo(fig, { y: index % 2 ? 65 : 95 }, {
            y: index % 2 ? -8 : -10,
            ease: 'none',
            scrollTrigger: { trigger: fig, start: 'top bottom', end: 'bottom top', scrub: 1.1 },
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

      // Venue promotions: bring the cards in gently and let the poster images settle into place.
      const venuePromos = root.querySelector('.venue-promotions');
      const promoCards = venuePromos ? Array.from(venuePromos.querySelectorAll('.promotion-card')) : [];
      if (venuePromos && promoCards.length) {
        const headerItems = venuePromos.querySelectorAll('.eyebrow, .promotion-note');
        if (headerItems.length) {
          gsap.from(headerItems, {
            y: 22,
            opacity: 0,
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

        promoCards.forEach((card, index) => {
          const imageFrame = card.querySelector('.promotion-card-image-frame');
          gsap.fromTo(card,
            { y: 34, opacity: 0, scale: 0.985 },
            {
              y: 0,
              opacity: 1,
              scale: 1,
              duration: 0.9,
              delay: index * 0.12,
              ease: 'power3.out',
              scrollTrigger: { trigger: card, start: 'top 84%', once: true }
            }
          );
          if (imageFrame) {
            gsap.fromTo(imageFrame,
              { scale: 1.02, yPercent: 0.5 },
              {
                scale: 1,
                yPercent: 0,
                duration: 0.9,
                delay: index * 0.08 + 0.04,
                ease: 'power2.out',
                scrollTrigger: { trigger: card, start: 'top 82%', once: true }
              }
            );
          }
        });
      }

      // Home experience: reveal the floating mosaic one image at a time.
      const museum = root.querySelector('.museum-experience');
      if (museum) {
        const exhibits = Array.from(museum.querySelectorAll('[data-museum-item]'));
        const intro = museum.querySelector('[data-museum-copy]');

        if (intro) {
          const introItems = intro.querySelectorAll('.eyebrow, h2, p:not(.eyebrow), .museum-gallery-link');
          if (introItems.length) {
            gsap.from(introItems, {
              y: 12,
              opacity: 0.9,
              duration: 0.75,
              stagger: 0.07,
              ease: 'power2.out',
              scrollTrigger: { trigger: intro, start: 'top 86%', once: true }
            });
          }
        }

        exhibits.forEach((exhibit, index) => {
          const direction = index % 2 === 0 ? 1 : -1;
          const finalOpacity = Number.parseFloat(window.getComputedStyle(exhibit).opacity) || 1;

          gsap.fromTo(exhibit, {
            x: direction * 12,
            y: 10,
            scale: 0.985,
            opacity: finalOpacity * 0.55,
          }, {
            x: 0,
            y: 0,
            scale: 1,
            opacity: finalOpacity,
            duration: 0.9,
            delay: index * 0.045,
            ease: 'power2.out',
            scrollTrigger: { trigger: exhibit, start: 'top 90%', once: true }
          });

          const image = exhibit.querySelector('img');
          if (!image) return;
          if (galleryDesktop) {
            gsap.fromTo(image,
              { yPercent: 2.5, scale: 1.035 },
              {
                yPercent: -1.5,
                scale: 1.015,
                ease: 'none',
                scrollTrigger: { trigger: exhibit, start: 'top bottom', end: 'bottom top', scrub: 0.45 }
              }
            );
          }
        });

        if (galleryDesktop && window.matchMedia('(hover: hover) and (pointer: fine)').matches) {
          const floatTargets = exhibits.map((exhibit, index) => ({
            x: gsap.quickTo(exhibit, 'x', { duration: 0.85, ease: 'power3.out' }),
            y: gsap.quickTo(exhibit, 'y', { duration: 0.85, ease: 'power3.out' }),
            depth: Number(exhibit.dataset.floatDepth) || 10,
            xDirection: index % 2 === 0 ? -1 : 1,
            yDirection: index % 3 === 0 ? 1 : -1
          }));

          const moveWithPointer = event => {
            const bounds = museum.getBoundingClientRect();
            if (!bounds.width || !bounds.height) return;
            const pointerX = ((event.clientX - bounds.left) / bounds.width - 0.5) * 2;
            const pointerY = ((event.clientY - bounds.top) / bounds.height - 0.5) * 2;
            floatTargets.forEach(target => {
              target.x(pointerX * target.depth * target.xDirection);
              target.y(pointerY * target.depth * 0.65 * target.yDirection);
            });
          };
          const resetFloat = () => floatTargets.forEach(target => { target.x(0); target.y(0); });

          museum.addEventListener('pointermove', moveWithPointer, { passive: true });
          museum.addEventListener('pointerleave', resetFloat, { passive: true });
          cleanup.push(() => {
            museum.removeEventListener('pointermove', moveWithPointer);
            museum.removeEventListener('pointerleave', resetFloat);
          });
        }
      }

      const experiencePhotos = Array.from(root.querySelectorAll('[data-experience-photo]'));
      experiencePhotos.forEach((photo, index) => {
        window.HoleInHubMotion.parallax(photo.querySelector('.photo-frame img'), {
          trigger: photo, depth: desktop ? 14 : 9,
        });
        gsap.from(photo, {
          y: 16,
          opacity: 0.9,
          duration: 0.82,
          delay: index * 0.07,
          ease: 'power2.out',
          scrollTrigger: { trigger: photo, start: 'top 88%', once: true }
        });
      });

      const experienceIntro = root.querySelector('[data-experience-intro]');
      if (experienceIntro) {
        gsap.from(experienceIntro, {
          y: 12,
          opacity: 0.92,
          duration: 0.78,
          ease: 'power2.out',
          scrollTrigger: { trigger: experienceIntro, start: 'top 88%', once: true }
        });
      }

      const experienceGallery = root.querySelector('[data-experience-gallery]');
      const galleryPanel = experienceGallery?.querySelector('[data-experience-gallery-panel]');
      const galleryViewport = galleryPanel?.querySelector('[data-experience-gallery-viewport]');
      const galleryTrack = experienceGallery?.querySelector('[data-experience-gallery-track]');
      const galleryCards = galleryTrack ? Array.from(galleryTrack.querySelectorAll('[data-gallery-card]')) : [];
      const galleryLinks = experienceGallery ? Array.from(experienceGallery.querySelectorAll('[data-gallery-target]')) : [];
      const galleryCurrent = experienceGallery?.querySelector('[data-gallery-current]');

      if (experienceGallery && galleryViewport && galleryTrack && galleryCards.length > 1) {
        let galleryTrigger;
        let activeGalleryIndex = -1;
        const setActiveGalleryIndex = value => {
          const index = Math.max(0, Math.min(galleryCards.length - 1, value));
          if (index === activeGalleryIndex) return;
          activeGalleryIndex = index;
          galleryCards.forEach((card, cardIndex) => card.classList.toggle('is-focused', cardIndex === index));
          galleryLinks.forEach((link, linkIndex) => {
            if (linkIndex === index) link.setAttribute('aria-current', 'true');
            else link.removeAttribute('aria-current');
          });
          if (galleryCurrent) galleryCurrent.textContent = String(index + 1).padStart(2, '0');
        };

        const navigateToGalleryCard = index => {
          const targetIndex = Math.max(0, Math.min(galleryCards.length - 1, index));
          if (desktop && galleryTrigger) {
            const scrollPosition = galleryTrigger.start + (galleryTrigger.end - galleryTrigger.start) * targetIndex / (galleryCards.length - 1);
            const smoother = window.ScrollSmoother?.get();
            if (smoother) smoother.scrollTo(scrollPosition, true);
            else window.scrollTo({ top: scrollPosition, behavior: 'smooth' });
            return;
          }
          galleryCards[targetIndex].scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
          setActiveGalleryIndex(targetIndex);
        };

        if (desktop) {
          experienceGallery.classList.add('experience-gallery-enhanced');
          cleanup.push(() => experienceGallery.classList.remove('experience-gallery-enhanced'));
          const getGalleryDistance = () => Math.max(0, galleryTrack.scrollWidth - galleryViewport.clientWidth);
          const galleryTimeline = gsap.timeline({
            scrollTrigger: {
              trigger: galleryPanel,
              start: 'top top+=104',
              end: () => `+=${getGalleryDistance()}`,
              pin: galleryPanel,
              scrub: 0.72,
              snap: {
                snapTo: 1 / (galleryCards.length - 1),
                duration: { min: 0.15, max: 0.45 },
                delay: 0.06,
                ease: 'power1.inOut',
              },
              anticipatePin: 1,
              invalidateOnRefresh: true,
              onUpdate: self => setActiveGalleryIndex(Math.round(self.progress * (galleryCards.length - 1))),
              onRefresh: self => setActiveGalleryIndex(Math.round(self.progress * (galleryCards.length - 1))),
            }
          });
          galleryTimeline.to(galleryTrack, { x: () => -getGalleryDistance(), duration: 1, ease: 'none' });
          galleryTrigger = galleryTimeline.scrollTrigger;
        } else {
          let galleryScrollFrame = 0;
          const updateMobileGalleryFocus = () => {
            galleryScrollFrame = 0;
            const viewportCenter = galleryViewport.getBoundingClientRect().left + galleryViewport.clientWidth / 2;
            let nearestIndex = 0;
            let nearestDistance = Number.POSITIVE_INFINITY;
            galleryCards.forEach((card, index) => {
              const bounds = card.getBoundingClientRect();
              const distance = Math.abs(bounds.left + bounds.width / 2 - viewportCenter);
              if (distance < nearestDistance) { nearestDistance = distance; nearestIndex = index; }
            });
            setActiveGalleryIndex(nearestIndex);
          };
          const onMobileGalleryScroll = () => {
            if (!galleryScrollFrame) galleryScrollFrame = requestAnimationFrame(updateMobileGalleryFocus);
          };
          galleryViewport.addEventListener('scroll', onMobileGalleryScroll, { passive: true });
          cleanup.push(() => {
            galleryViewport.removeEventListener('scroll', onMobileGalleryScroll);
            if (galleryScrollFrame) cancelAnimationFrame(galleryScrollFrame);
          });
        }

        galleryLinks.forEach((link, index) => {
          const onNavigate = event => {
            event.preventDefault();
            navigateToGalleryCard(index);
          };
          link.addEventListener('click', onNavigate);
          cleanup.push(() => link.removeEventListener('click', onNavigate));
        });

        const onGalleryKeydown = event => {
          if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
          event.preventDefault();
          navigateToGalleryCard(activeGalleryIndex + (event.key === 'ArrowRight' ? 1 : -1));
        };
        galleryViewport.addEventListener('keydown', onGalleryKeydown);
        cleanup.push(() => galleryViewport.removeEventListener('keydown', onGalleryKeydown));
        setActiveGalleryIndex(0);
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
              window.HoleInHubMotion.parallax(img, { trigger: frame, depth: 10, scrub: 1 });
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
        if (frame.hasAttribute('data-parallax') || frame.hasAttribute('data-motion')) return;
        gsap.from(frame, {
          scale: 0.82,
          clipPath: 'inset(12% 0% 12% 0%)',
          opacity: 0,
          duration: 1.1,
          ease: 'power3.out',
          scrollTrigger: { trigger: frame, start: 'top 92%', once: true },
        });
        if (image) {
          window.HoleInHubMotion.parallax(image, { trigger: frame });
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
            window.HoleInHubMotion.parallax(img, { trigger: card, depth: 3, scrub: 0.3 });
          }
        });
      }

      return () => {
        cleanup.splice(0).forEach(dispose => {
          try { dispose(); } catch {}
        });
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
let dispose;
let disposed = false;
const disposeComponents = window.HoleInHubMotion.mount(root);
const videoCleanup = [...root.querySelectorAll('video')].map(video => initBackgroundVideo(video, reduced));
const refresh = () => ScrollTrigger.refresh();
document.fonts.ready.then(() => {
  if (disposed) return;
  dispose = animatePage(root);
  refresh();
  document.dispatchEvent(new Event('holeinhub:motion-ready'));
});
window.addEventListener('pageshow', refresh);
window.addEventListener('pagehide', event => {
  if (event.persisted) return;
  disposed = true;
  dispose?.();
  disposeComponents();
  videoCleanup.forEach(cleanup => cleanup());
  window.removeEventListener('pageshow', refresh);
});
})();
