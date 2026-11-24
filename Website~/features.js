(function () {
  'use strict';

  var MEDIA = 'media/v4/';
  var $ = function (selector, root) { return (root || document).querySelector(selector); };
  var $$ = function (selector, root) { return Array.prototype.slice.call((root || document).querySelectorAll(selector)); };
  var page = $('.feature-studio');
  if (!page) return;
  var header = $('.hdr'), chapterNav = $('.studio-nav', page);
  var observers = [];
  function pageObserver(Type, callback, options) {
    var observer = new Type(callback, options);
    observers.push(observer);
    return observer;
  }
  document.addEventListener('ats:leave', function () {
    observers.forEach(function (observer) { observer.disconnect(); });
    if (chapterBarObserver) chapterBarObserver.disconnect();
    desktopPace.removeEventListener('change', changeDesktopPace);
  }, { once: true });
  function asset(file) { return MEDIA + file + '?v=' + encodeURIComponent(page.dataset.mediaVersion); }
  var flow = $('.studio-flow', page);
  var motion = matchMedia('(prefers-reduced-motion: reduce)');
  var phoneChapters = matchMedia('(max-width: 767px), (max-width: 950px) and (max-height: 540px) and (pointer: coarse)');
  var query = /[?&]view=(scroll|overview)(?:&|$)/.exec(location.search);
  var preferred = query ? query[1] : 'scroll';
  if (!query) {
    try { preferred = localStorage.getItem('ats-fview') || preferred; } catch (e) {}
  }
  var overview = false;
  var subscribers = [];
  var desktopPace = matchMedia('(min-width: 901px) and (hover: hover) and (pointer: fine)');
  var speedControl = $('.studio-scroll-speed', header);
  var savedSpeed = 1;
  try {
    var storedSpeed = Number(localStorage.getItem('ats-feature-speed'));
    if (storedSpeed === 2 || storedSpeed === 4) savedSpeed = storedSpeed;
  } catch (e) {}
  var scrollSpeed = desktopPace.matches ? savedSpeed : 1;
  var paceTracks = [];
  var sources = Object.create(null);
  var chapters = $$('.studio-section > .studio-section-head, .studio-map-example > .studio-section-head, .stack-head, .modes-head', flow);
  chapters.forEach(function (head) {
    if (!head.parentElement.matches('.studio-maps')) head.classList.add('studio-chapter-head');
  });

  function text(key, fallback) {
    return window.ATS && window.ATS.t(key) || fallback || '';
  }

  function modeForced() {
    return motion.matches || document.documentElement.classList.contains('perf') ||
      !!(navigator.connection && navigator.connection.saveData);
  }

  function navigationHeight() {
    return header.offsetHeight + chapterNav.offsetHeight;
  }

  function pinTop() {
    return navigationHeight() + 14;
  }

  function chapterHead(element) {
    var section = element.closest('.studio-map-example, .studio-section');
    return section && $('.studio-chapter-head', section);
  }

  function chapterTop(head) {
    if (!head) return pinTop();
    return navigationHeight() + head.offsetHeight + (parseFloat(getComputedStyle(head).marginBottom) || 0);
  }

  var chapterBarObserver, chapterBarMargin;
  function refreshChapterTop() {
    var top = navigationHeight();
    flow.style.setProperty('--chapter-top', top + 'px');
    if (!('IntersectionObserver' in window)) return;
    var margin = -top + 'px 0px ' + -Math.max(0, document.documentElement.clientHeight - top - 1) + 'px 0px';
    if (margin === chapterBarMargin) return;
    chapterBarMargin = margin;
    if (chapterBarObserver) chapterBarObserver.disconnect();
    chapterBarObserver = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var head = $(':scope > .studio-chapter-head', entry.target);
        if (head) head.dataset.docked = String(entry.isIntersecting);
      });
    }, { rootMargin: margin });
    chapters.forEach(function (head) {
      if (head.classList.contains('studio-chapter-head')) chapterBarObserver.observe(head.parentElement);
    });
  }

  function currentSection() {
    var sections = $$('.studio-section, .studio-map-example', flow);
    var target = pinTop();
    for (var i = sections.length - 1; i >= 0; i--) {
      var rect = sections[i].getBoundingClientRect();
      if (rect.top <= target + 20 && rect.bottom > target) return sections[i];
    }
    return null;
  }

  var previousLinks = {
    'all-features': 'select', 'feature-index': 'select', 'sel-plate': 'select',
    'crest-tour': 'editor', 'stack': 'layers', 'more-workflows': 'maps',
    'undo': 'history-example', 'files': 'safety', 'files-new': 'safety', 'report': 'audit-example',
    'feature-index-target-ao': 'studio-ao', 'feature-index-target-normal': 'material',
    'feature-index-target-poiyomi': 'material', 'feature-index-target-standard': 'studio-material-tools',
    'feature-index-target-copy': 'studio-material-tools', 'feature-index-target-colour-match': 'colour',
    'feature-index-target-colour-controls': 'studio-colour-tools', 'feature-index-target-drag': 'select',
    'feature-index-target-wand': 'select', 'feature-index-target-scope': 'select',
    'feature-index-target-fill': 'studio-paint-tools', 'feature-index-target-mirror': 'studio-paint-tools',
    'feature-index-target-stops': 'studio-paint-tools', 'feature-index-target-masks': 'modes',
    'crest-step-pick': 'select', 'crest-step-select': 'select', 'crest-step-paint': 'paint',
    'crest-step-fill': 'studio-paint-tools', 'crest-step-gradient': 'studio-paint-tools', 'crest-step-layers': 'layers',
    'crest-step-map': 'maps', 'crest-step-material': 'material', 'crest-step-final': 'editor'
  };

  function followPreviousLink() {
    var id = previousLinks[location.hash.slice(1)];
    if (!id || typeof id !== 'string') return;
    var target = document.getElementById(id);
    if (!target) return;
    if (target.tagName === 'DETAILS') target.open = true;
    target.scrollIntoView({ block: 'start', behavior: 'instant' });
  }

  var scrollButton = $('[data-studio-view="scroll"]', page);
  function paceTrack(element, measure) {
    var track = { element: element, measure: measure, top: 0, travel: 0 };
    paceTracks.push(track);
    return track;
  }

  function setScrollSpeed(speed) {
    if (speed === scrollSpeed) return;
    var nearest, position, travel, gap = Infinity;
    paceTracks.forEach(function (track) {
      if (!track.travel) return;
      var offset = track.top - track.element.getBoundingClientRect().top;
      var distance = Math.max(0, -offset, offset - track.travel);
      if (distance < gap) {
        nearest = track;
        position = offset;
        travel = track.travel;
        gap = distance;
      }
    });
    scrollSpeed = speed;
    paceTracks.forEach(function (track) { track.measure(); });
    if (nearest && nearest.travel) {
      var fraction = Math.min(1, Math.max(0, position / travel));
      var next = position + fraction * (nearest.travel - travel);
      window.scrollTo({ top: scrollY + nearest.element.getBoundingClientRect().top - nearest.top + next, behavior: 'instant' });
    }
  }

  function showSpeedControl() {
    if (speedControl) speedControl.hidden = overview || !desktopPace.matches;
  }
  function changeDesktopPace() {
    showSpeedControl();
    setScrollSpeed(desktopPace.matches ? savedSpeed : 1);
  }
  if (speedControl) {
    $('input[value="' + savedSpeed + '"]', speedControl).checked = true;
    speedControl.addEventListener('change', function (event) {
      if (!event.target.matches('input:checked')) return;
      savedSpeed = Number(event.target.value);
      try { localStorage.setItem('ats-feature-speed', savedSpeed); } catch (e) {}
      setScrollSpeed(savedSpeed);
    });
    desktopPace.addEventListener('change', changeDesktopPace);
  }
  function refreshScrollTitle() {
    if (scrollButton) scrollButton.title = scrollButton.disabled ? text('fv.forced') : '';
  }

  function mode(name, keepPlace) {
    var anchor = keepPlace && currentSection();
    var offset = anchor ? Math.min(0, anchor.getBoundingClientRect().top - pinTop()) : 0;
    var opening = $('#editor', page);
    var inOpening = keepPlace && !anchor && opening.getBoundingClientRect().bottom > pinTop();
    var forced = modeForced();
    overview = forced || name === 'overview';
    showSpeedControl();
    flow.toggleAttribute('data-overview', overview);
    flow.toggleAttribute('data-chapters', !overview);
    $$('#jump [data-studio-view]', page).forEach(function (button) {
      var scrolling = button.dataset.studioView === 'scroll';
      button.setAttribute('aria-pressed', String(scrolling !== overview));
      button.disabled = forced && scrolling;
    });
    refreshScrollTitle();
    refreshChapterTop();
    subscribers.forEach(function (update) { update(); });
    if (anchor) {
      var maxOffset = Math.max(0, anchor.offsetHeight - pinTop());
      window.scrollTo({ top: window.scrollY + anchor.getBoundingClientRect().top - pinTop() - Math.max(-maxOffset, offset), behavior: 'instant' });
    } else if (inOpening && overview) {
      window.scrollTo({ top: 0, behavior: 'instant' });
    }
  }

  var view = $('.studio-modes', page);
  if (view) {
    view.hidden = false;
    $$('button[data-studio-view]', view).forEach(function (button) {
      button.addEventListener('click', function () {
        preferred = button.dataset.studioView;
        var address = new URL(location.href);
        address.searchParams.set('view', preferred);
        history.replaceState(history.state, '', address.href);
        try { localStorage.setItem('ats-fview', preferred); } catch (e) {}
        mode(preferred, true);
      });
    });
  }
  mode(preferred, false);
  window.addEventListener('resize', refreshChapterTop, { passive: true });
  if ('ResizeObserver' in window) {
    var navSize = pageObserver(ResizeObserver, refreshChapterTop);
    navSize.observe(header);
    navSize.observe(chapterNav);
  }
  document.addEventListener('ats:lang', refreshScrollTitle);
  motion.addEventListener('change', function () { mode(preferred, true); });
  if ('MutationObserver' in window) {
    pageObserver(MutationObserver, function () {
      if (overview !== (preferred === 'overview' || modeForced())) mode(preferred, true);
    }).observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  }

  function near(element, load) {
    if (!element) return;
    if (!('IntersectionObserver' in window)) { load(); return; }
    var observer = pageObserver(IntersectionObserver, function (entries) {
      if (!entries.some(function (entry) { return entry.isIntersecting; })) return;
      observer.disconnect();
      load();
    }, { rootMargin: '850px 0px' });
    observer.observe(element);
  }

  function loadJson(file) {
    return fetch(asset(file)).then(function (response) {
      if (!response.ok) throw Error(response.status);
      return response.json();
    });
  }

  function preload(file) {
    if (!sources[file]) {
      sources[file] = new Promise(function (resolve) {
        var image = new Image();
        image.onload = function () {
          if (image.decode) image.decode().then(function () { resolve(true); }, function () { resolve(false); });
          else resolve(true);
        };
        image.onerror = function () { resolve(false); };
        image.src = asset(file);
      });
    }
    return sources[file];
  }

  function arrowKeys(buttons) {
    buttons.forEach(function (button, index) {
      button.addEventListener('keydown', function (event) {
        var code = event.key;
        var step = code === 'ArrowRight' || code === 'ArrowDown' ? 1 : code === 'ArrowLeft' || code === 'ArrowUp' ? -1 : 0;
        var next = code === 'Home' ? 0 : code === 'End' ? buttons.length - 1 : (index + step + buttons.length) % buttons.length;
        if (!step && code !== 'Home' && code !== 'End') return;
        event.preventDefault();
        buttons[next].focus();
        buttons[next].click();
      });
    });
  }

  function chapterTitle(head) {
    var heading = $('h2, h3', head);
    return $('[data-i18n]', heading) || heading;
  }

  function useChapterTitle(label, title) {
    var key = title.getAttribute('data-i18n');
    if (key) label.setAttribute('data-i18n', key);
    label.textContent = text(key, title.textContent.trim());
  }

  function updateChapterHref(link) {
    link.href = location.pathname + location.search + '#' + link.dataset.chapterTarget;
  }

  function refreshChapterLinks() {
    requestAnimationFrame(function () { $$('.studio-chapter-next', page).forEach(updateChapterHref); });
  }
  window.addEventListener('ats-tint', refreshChapterLinks);
  document.addEventListener('ats:lang', refreshChapterLinks);

  function nextChapter(head, stage) {
    var index = chapters.indexOf(head);
    var next = index < 0 ? null : chapters[index + 1];
    if (!next) return null;
    var section = next.closest('.studio-map-example, .studio-section');
    var link = make('a', 'studio-chapter-next');
    link.dataset.chapterTarget = section.id;
    updateChapterHref(link);
    var prompt = make('span', 'next-tag');
    prompt.setAttribute('data-i18n', 'next.next');
    prompt.textContent = text('next.next', 'Next');
    var title = make('span', 'next-name');
    useChapterTitle(title, chapterTitle(next));
    var arrow = make('span', 'next-go');
    arrow.textContent = '→';
    arrow.setAttribute('aria-hidden', 'true');
    link.append(prompt, ' ', title, arrow);
    link.addEventListener('click', function (event) {
      if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      requestAnimationFrame(function () {
        var heading = $('h2, h3', next);
        heading.tabIndex = -1;
        heading.focus({ preventScroll: true });
      });
      if (link.hash === location.hash) settleLink();
    });
    stage.appendChild(link);
    return link;
  }

  function showNextChapter(link, visible) {
    if (link) link.classList.toggle('is-next', phoneChapters.matches && visible);
  }

  function resultHold(height) { return Math.round(Math.max(180, Math.min(520, height * .55))); }

  function initOpening() {
    var opening = $('#editor', page);
    var stage = $('.studio-opening-stage', opening);
    var header = $('.hdr');
    var chapters = $('.studio-opening-chapters', opening);
    var scrub = $('.studio-chapter-scrub', chapters);
    var chapterList = $('ol', chapters);
    var art = $('.studio-opening-art', opening);
    var pictures = $$('img:not([data-opening-normal-off])', art);
    var normalOff = $('[data-opening-normal-off]', art);
    var panels = $$('.studio-opening-beat', opening);
    var buttons = $$('button', chapters);
    var canPin = matchMedia('(min-width: 321px) and (min-height: 640px), (min-width: 901px) and (min-height: 580px)');
    var last = panels.length - 1;
    var normalBeat = last - 1;
    var normalCompare = $('[data-opening-compare]', panels[normalBeat]);
    var metalCompare = $('[data-opening-compare]', panels[last]);
    var explore = $('.studio-opening-links a [data-i18n]', opening);
    var exploreLabel = explore.textContent;
    var selectTitle = chapterTitle(chapterHead($('#select', flow)));
    var active = 0, target = 0, shown = pictures[0];
    var top = 0, step = 1, tail = 0, pinned = false, close = true, raf = 0;
    var pace = paceTrack(opening, measure);
    var decoded = new Set();
    var decoding = new Set();
    var artBounds = null;
    shown.classList.add('is-current');
    art.classList.add('studio-opening-blend');
    $$('img', art).forEach(function (picture) { picture.hidden = false; });

    function showNextTitle(atEnd) {
      var key = atEnd ? selectTitle.getAttribute('data-i18n') : 'studio.opening.skip';
      if (explore.getAttribute('data-i18n') === key) return;
      explore.setAttribute('data-i18n', key);
      explore.textContent = text(key, atEnd ? selectTitle.textContent.trim() : exploreLabel);
    }

    function fitArt() {
      var source = pictures[0];
      if (!source.complete || !source.naturalWidth || !art.clientWidth || !art.clientHeight) return;
      if (!artBounds) {
        try {
          var sample = document.createElement('canvas');
          sample.width = 400; sample.height = 225;
          var pixels = sample.getContext('2d', { willReadFrequently: true });
          pixels.drawImage(source, 0, 0, sample.width, sample.height);
          var bytes = pixels.getImageData(0, 0, sample.width, sample.height).data;
          var left = sample.width, right = -1, topEdge = sample.height, bottom = -1;
          for (var y = 0; y < sample.height; y++) for (var x = 0; x < sample.width; x++) {
            if (bytes[(y * sample.width + x) * 4 + 3] <= 8) continue;
            left = Math.min(left, x); right = Math.max(right, x);
            topEdge = Math.min(topEdge, y); bottom = Math.max(bottom, y);
          }
          if (right <= left || bottom <= topEdge) return;
          artBounds = [left / sample.width, topEdge / sample.height, (right - left + 1) / sample.width, (bottom - topEdge + 1) / sample.height];
          art.dataset.subjectBounds = artBounds.join(',');
        } catch (e) { return; }
      }
      var width = art.clientWidth, height = art.clientHeight;
      var base = Math.min(width / source.naturalWidth, height / source.naturalHeight);
      var scale = Math.min(width * .96 / (artBounds[2] * source.naturalWidth * base), height * .96 / (artBounds[3] * source.naturalHeight * base));
      art.style.setProperty('--opening-shadow-y', (artBounds[3] * source.naturalHeight * base * scale * .5).toFixed(2) + 'px');
      art.style.setProperty('--opening-shadow-width', (artBounds[2] * source.naturalWidth * base * scale * .87).toFixed(2) + 'px');
      art.style.setProperty('--opening-scale', scale.toFixed(5));
      art.style.setProperty('--opening-offset-x', (-(artBounds[0] + artBounds[2] / 2 - .5) * source.naturalWidth * base * scale).toFixed(2) + 'px');
      art.style.setProperty('--opening-offset-y', (-(artBounds[1] + artBounds[3] / 2 - .5) * source.naturalHeight * base * scale).toFixed(2) + 'px');
      art.dataset.subjectFit = 'ready';
    }

    function load(image) {
      if (!image.hasAttribute('src')) image.src = asset(image.dataset.openingImage);
      if (decoded.has(image)) return true;
      if (image.decode && !decoding.has(image)) {
        decoding.add(image);
        image.decode().then(function () {
          decoded.add(image);
          if (image === pictures[0]) fitArt();
          schedule();
        }).catch(function () {});
      }
      return !image.decode && image.complete && image.naturalWidth > 0;
    }

    function artwork(index) {
      if (index === normalBeat && normalCompare.getAttribute('aria-checked') === 'false') return normalOff;
      if (index === last && metalCompare.getAttribute('aria-checked') === 'false') return pictures[normalBeat];
      return pictures[index];
    }

    function render(position) {
      var index = Math.round(position);
      var value = Number(position.toFixed(2));
      if (scrub.valueAsNumber !== value) scrub.valueAsNumber = value;
      var name = buttons[index].textContent.trim();
      if (scrub.getAttribute('aria-valuetext') !== name) scrub.setAttribute('aria-valuetext', name);
      chapters.style.setProperty('--opening-progress', String(position / last));
      var picture = artwork(index);
      var ready = load(picture);
      if (pinned && index < last) load(pictures[index + 1]);
      if (index === last) load(pictures[normalBeat]);
      if (!ready) return;
      if (picture !== shown) {
        shown.classList.remove('is-current');
        picture.classList.add('is-current');
        shown = picture;
      }
      if (index === active) return;
      var panel = panels[index];
      $$('[data-opening-image]', panel).forEach(load);
      panels[active].hidden = true;
      panel.style.setProperty('--beat-lift', index > active ? '12px' : '-12px');
      buttons[active].removeAttribute('aria-current');
      buttons[index].setAttribute('aria-current', 'step');
      panel.hidden = false;
      opening.dataset.openingBeat = index;
      active = index;
    }

    function update() {
      raf = 0;
      if (!close) return;
      var bounds = opening.getBoundingClientRect();
      if (bounds.bottom <= top) return;
      var position = pinned ? Math.min(last, Math.max(0, (top - bounds.top) / step)) : target;
      if (position >= normalBeat - .6 && position < last) load(normalOff);
      render(position);
      showNextTitle(phoneChapters.matches && pinned && top - bounds.top >= last * step + tail * .7);
    }

    function schedule() {
      if (!raf) raf = requestAnimationFrame(update);
    }

    function measure() {
      var next = !overview && canPin.matches;
      if (pinned && !next) target = active;
      pinned = next;
      top = header.offsetHeight;
      opening.style.setProperty('--opening-top', top + 'px');
      opening.toggleAttribute('data-scroll', pinned);
      step = Math.round(Math.min(495, Math.max(270, stage.offsetHeight * .58))) / scrollSpeed;
      tail = resultHold(stage.offsetHeight) / scrollSpeed;
      pace.top = top;
      pace.travel = pinned ? last * step + tail : 0;
      opening.style.setProperty('--opening-run', (last * step + tail) + 'px');
      fitArt();
      schedule();
    }

    function seek(position) {
      target = Math.max(0, Math.min(last, position));
      if (pinned) {
        window.scrollTo({ top: scrollY + opening.getBoundingClientRect().top - top + target * step, behavior: 'instant' });
      }
      schedule();
    }
    scrub.max = last;
    scrub.addEventListener('input', function () { seek(scrub.valueAsNumber); });
    scrub.addEventListener('keydown', function (event) {
      var index = Math.round(scrub.valueAsNumber);
      switch (event.key) {
        case 'ArrowRight': case 'ArrowUp': case 'PageUp': index++; break;
        case 'ArrowLeft': case 'ArrowDown': case 'PageDown': index--; break;
        case 'Home': index = 0; break;
        case 'End': index = last; break;
        default: return;
      }
      event.preventDefault();
      seek(index);
    });
    var drag = null, dragged = false;
    chapterList.addEventListener('pointerdown', function (event) {
      if (!event.isPrimary || event.button !== 0) return;
      var rect = chapterList.getBoundingClientRect();
      var pitch = rect.width / buttons.length;
      var index = buttons.indexOf(event.target.closest('button'));
      drag = { id: event.pointerId, x: event.clientX, y: event.clientY, pitch: pitch,
        start: index < 0 ? (event.clientX - rect.left) / pitch - .5 : index };
      dragged = false;
    });
    chapterList.addEventListener('pointermove', function (event) {
      if (!drag || event.pointerId !== drag.id) return;
      var dx = event.clientX - drag.x, dy = event.clientY - drag.y;
      if (!dragged) {
        if (Math.max(Math.abs(dx), Math.abs(dy)) < 5) return;
        if (Math.abs(dx) < Math.abs(dy)) { drag = null; return; }
        chapterList.setPointerCapture(event.pointerId);
        dragged = true;
      }
      seek(drag.start + dx / drag.pitch);
    });
    function release(event) {
      if (drag && drag.id === event.pointerId) drag = null;
    }
    chapterList.addEventListener('pointerup', release);
    chapterList.addEventListener('pointercancel', release);
    chapterList.addEventListener('pointerleave', function () { if (!dragged) drag = null; });

    arrowKeys(buttons);
    [normalCompare, metalCompare].forEach(function (button) {
      button.addEventListener('click', function () {
        button.setAttribute('aria-checked', String(button.getAttribute('aria-checked') !== 'true'));
        schedule();
      });
    });
    buttons.forEach(function (button, index) {
      button.addEventListener('click', function (event) {
        if (!dragged || event.detail === 0) seek(index);
      });
    });
    pictures.forEach(function (picture) { picture.addEventListener('load', function () { fitArt(); schedule(); }); });
    chapters.hidden = false;
    opening.dataset.openingBeat = '0';
    subscribers.push(measure);
    document.addEventListener('ats:lang', schedule);
    window.addEventListener('scroll', function () { if (pinned && close) schedule(); }, { passive: true });
    window.addEventListener('resize', measure, { passive: true });
    if ('ResizeObserver' in window) pageObserver(ResizeObserver, measure).observe(header);
    if ('ResizeObserver' in window) pageObserver(ResizeObserver, fitArt).observe(art);
    if ('IntersectionObserver' in window) {
      pageObserver(IntersectionObserver, function (entries) {
        close = entries[entries.length - 1].isIntersecting;
        if (close) schedule();
      }, { rootMargin: '240px 0px' }).observe(opening);
    }
    measure();
  }

  $$('[data-studio-pick]', page).forEach(function (root) {
    var buttons = $$('button[data-image]', root);
    var images = $$('[data-studio-image]', root);
    var sample = $('[data-studio-sample]', root);
    var caption = $('[data-studio-caption]', root);
    var request = 0, wanted = -1, journey = null;
    var track = $('[data-scroll-pick]', root);
    arrowKeys(buttons);
    function choose(index) {
        if (wanted === index) return;
        wanted = index;
        var button = buttons[index];
        var file = button.dataset.image;
        var sampleFile = button.dataset.sample;
        var id = ++request;
        var files = sample && sampleFile ? [file, sampleFile] : [file];
        Promise.all(files.map(preload)).then(function (loaded) {
          if (request !== id || !loaded.every(Boolean)) return;
          root.dataset.currentImage = file;
          images.forEach(function (image) { image.src = asset(file); });
          if (sample && sampleFile) { sample.src = asset(sampleFile); arrive(sample.parentElement); }
          buttons.forEach(function (control) { control.setAttribute('aria-pressed', String(control === button)); });
          if (caption) {
            caption.setAttribute('data-i18n', button.dataset.caption);
            caption.textContent = text(button.dataset.caption, caption.textContent);
            arrive(caption);
          }
        });
    }
    buttons.forEach(function (button, index) {
      button.addEventListener('click', function () {
        choose(index);
        if (journey) journey.jump((index + .5) / buttons.length);
      });
    });
    if (track) {
      function measure() { reserveCaption(root, buttons); }
      measure();
      journey = scrollStudy(track, function (value) { choose(Math.min(buttons.length - 1, Math.floor(value * buttons.length))); }, buttons.length, .5);
      window.addEventListener('resize', measure, { passive: true });
      document.addEventListener('ats:lang', measure);
    }
  });

  $$('[data-studio-reveal]', page).forEach(function (link) {
    link.addEventListener('click', function () {
      var detail = document.getElementById(link.dataset.studioReveal);
      if (detail) detail.open = true;
    });
  });

  var savePanelLink = $('[data-panel-link]', page);
  var savePanelImage = $('.studio-save-panel', page);
  if (savePanelLink && savePanelImage) {
    savePanelImage.addEventListener('load', function () {
      savePanelLink.href = savePanelImage.currentSrc || savePanelImage.src;
    });
  }

  function make(tagName, className) {
    var element = document.createElement(tagName);
    if (className) element.className = className;
    return element;
  }

  function studioPath(file) { return file.indexOf('studio/') === 0 ? file : 'studio/' + file; }

  function studyNearby(track) {
    var bounds = track.getBoundingClientRect();
    return bounds.bottom > -400 && bounds.top < innerHeight + 400;
  }

  function arrive(node) {
    if (!node || modeForced() || !node.animate) return;
    if (node._studioArrival) node._studioArrival.cancel();
    node._studioArrival = node.animate([
      { opacity: .35, transform: 'translateY(5px)' },
      { opacity: 1, transform: 'translateY(0)' }
    ], { duration: 190, easing: 'cubic-bezier(.2,.7,.2,1)' });
  }

  function reserveCaption(root, buttons) {
    var caption = $('[data-studio-caption]', root);
    if (!caption || !buttons.length || !caption.clientWidth) return;
    var probe = caption.cloneNode(false);
    probe.removeAttribute('data-studio-caption');
    probe.removeAttribute('data-i18n');
    probe.removeAttribute('aria-live');
    probe.style.cssText = 'position:absolute;visibility:hidden;pointer-events:none;min-height:0;width:' + caption.clientWidth + 'px';
    caption.parentNode.appendChild(probe);
    var height = 0;
    buttons.forEach(function (button) {
      probe.textContent = text(button.dataset.caption);
      height = Math.max(height, probe.offsetHeight);
    });
    probe.remove();
    caption.style.minHeight = Math.ceil(height) + 'px';
  }

  function scrollStudy(track, update, steps, initial) {
    var stage = $('.studio-motion-stage', track);
    var head = chapterHead(track);
    var content = make('div', 'studio-motion-content');
    while (stage.firstChild) content.appendChild(stage.firstChild);
    stage.appendChild(content);
    var next = nextChapter(head, stage);
    var nearby = false, raf = 0, top = 0, distance = 1, manual = initial || 0;
    var lead, tail;
    var pace = paceTrack(track, measure);
    var inspectResult = track.hasAttribute('data-hold-result');
    var pinned = false;
    function render() {
      raf = 0;
      if (!nearby) return;
      var position = top - track.getBoundingClientRect().top;
      var progress = pinned ? Math.min(1, Math.max(0, (position - lead) / distance)) : manual;
      showNextChapter(next, pinned && (inspectResult ? position >= lead + distance + tail * .7 : progress >= .88));
      track.dataset.scrollPhase = !pinned ? 'manual' : position < 0 ? 'enter' : progress < 1 ? 'active' : position <= lead + distance + tail ? 'complete' : 'leave';
      track.dataset.scrollProgress = progress.toFixed(4);
      update(progress);
    }
    function schedule() {
      nearby = studyNearby(track);
      if (nearby && !raf) raf = requestAnimationFrame(render);
    }
    function measure() {
      top = chapterTop(head);
      pinned = !overview;
      track.toggleAttribute('data-scroll-study', pinned);
      var nextSpace = phoneChapters.matches && pinned && next ? next.offsetHeight + 8 : 0;
      var available = Math.max(1, innerHeight - top - nextSpace - 12);
      lead = 36;
      tail = 64;
      if (inspectResult) {
        lead = Math.round(Math.max(60, Math.min(140, available * .15)));
        distance = Math.round(Math.max(670, Math.min(2400, available * (.9 + steps * .26))));
        tail = resultHold(available);
      } else if (track.hasAttribute('data-recording')) {
        distance = Math.round(Math.max(1800, Math.min(3600, available * 3.5)));
      } else {
        distance = Math.round(Math.max(440, Math.min(1080, available * (.5 + steps * .14))));
      }
      lead /= scrollSpeed;
      distance /= scrollSpeed;
      tail /= scrollSpeed;
      pace.top = top;
      pace.travel = pinned ? lead + distance + tail : 0;
      track.style.setProperty('--study-top', top + 'px');
      track.style.setProperty('--study-available', Math.round(available) + 'px');
      var height = content.offsetHeight;
      var scale = pinned ? Math.min(1, available / Math.max(1, height)) : 1;
      var artHeight = Math.ceil(height * scale);
      stage.style.setProperty('--study-scale', scale.toFixed(5));
      stage.style.setProperty('--study-stage-height', artHeight + nextSpace + 'px');
      track.style.setProperty('--study-extra', pinned ? lead + distance + tail + 'px' : '0px');
      track.dataset.scrollRun = String(distance);
      track.dataset.scrollLead = String(lead);
      schedule();
    }
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', measure, { passive: true });
    document.addEventListener('ats:lang', measure);
    subscribers.push(measure);
    if ('ResizeObserver' in window) {
      var layout = pageObserver(ResizeObserver, measure);
      layout.observe(content);
      if (head) layout.observe(head);
    }
    if (document.fonts) document.fonts.ready.then(measure);
    if ('IntersectionObserver' in window) pageObserver(IntersectionObserver, function (entries) {
      nearby = entries[entries.length - 1].isIntersecting;
      if (nearby) schedule();
    }, { rootMargin: '400px 0px' }).observe(track);
    else nearby = true;
    measure();
    return {
      measure: measure,
      jump: function (value) {
        manual = Math.max(0, Math.min(1, value));
        update(manual);
        if (pinned) window.scrollTo({ top: scrollY + track.getBoundingClientRect().top - top + lead + manual * distance, behavior: 'instant' });
      }
    };
  }

  function initScrollComparisons() {
    $$('[data-scroll-compare]', page).forEach(function (track) {
      var cmp = $('.cmp', track);
      var range = $('.cmp-range', cmp);
      var colourMatch = track.dataset.scrollCompare === 'colour';
      var mapControls = $('.studio-map-controls', track);
      var panel = mapControls && $('.studio-map-panel', mapControls);
      var narrow = matchMedia('(max-width: 650px)');
      var manualSeam = Number(cmp.dataset.cmp) || 50;
      var wasOverview;
      var pickedDepth = null;
      function setSeam(seam) {
        range.value = seam.toFixed(2);
        cmp.style.setProperty('--seam', seam.toFixed(2));
        range.dispatchEvent(new Event('input', { bubbles: true }));
      }
      function syncMode() {
        if (panel) {
          if (!overview && narrow.matches && panel.parentElement === mapControls) {
            track.insertAdjacentElement('afterend', panel);
          } else if ((overview || !narrow.matches) && panel.parentElement !== mapControls) mapControls.appendChild(panel);
        }
        if (overview === wasOverview) return;
        pickedDepth = null;
        range.tabIndex = overview ? 0 : -1;
        if (wasOverview) manualSeam = Number(range.value);
        wasOverview = overview;
        if (overview) setSeam(manualSeam);
      }
      subscribers.push(syncMode);
      if (panel) narrow.addEventListener('change', syncMode);
      if (colourMatch) cmp.addEventListener('studio:match-picked', function () {
        if (overview) { if (range.value > 70) setSeam(50); return; }
        pickedDepth = Number(track.dataset.scrollProgress) || 0;
        cmp.style.setProperty('--colour-before', '0');
        setSeam(0);
      });
      syncMode();
      scrollStudy(track, function (value) {
        if (!overview) {
          if (pickedDepth !== null) {
            pickedDepth = Math.max(pickedDepth, value);
            value = pickedDepth ? value / pickedDepth : 1;
            if (value === 0) pickedDepth = null;
          }
          if (colourMatch) cmp.style.setProperty('--colour-before', (1 - value).toFixed(3));
          setSeam(100 * (1 - value));
        }
      }, 3, .5);
    });
  }

  function initColourMatch() {
    var section = $('#colour', page);
    var reference = $('.studio-reference', section);
    var image = $('.cmp-saved img', section);
    var home = $('.studio-palette-home'), picker = $('.hdr-looks', home);
    var dock = $('.studio-palette-dock', reference);
    var name = $('[data-reference-name]', reference);
    var hex = $('[data-reference-hex]', reference);
    var choices = Object.create(null);
    $$('template[data-match-look]', reference).forEach(function (choice) {
      choices[choice.dataset.matchLook] = {
        colour: choice.dataset.matchHex,
        file: choice.content.querySelector('img').getAttribute('src').slice(MEDIA.length).split('?')[0]
      };
    });

    var compactHeader = matchMedia('(max-width: 439px)');
    var smallScreen = matchMedia('(max-width: 767px), (pointer: coarse)');
    var flight, frame, observer, observedMargin;
    function stopFlight() {
      cancelAnimationFrame(frame);
      if (flight) flight.remove();
      flight = null;
    }
    function canFly() { return !modeForced() && !smallScreen.matches && !document.hidden; }
    function fly(start) {
      var end = picker.getBoundingClientRect();
      if (!canFly() || start.bottom < -42 || start.top > innerHeight + 42 || end.bottom < 0 || end.top > innerHeight) return;
      var strip = document.createElement('div');
      strip.className = 'studio-palette-flight';
      strip.inert = true;
      strip.setAttribute('aria-hidden', 'true');
      strip.style.width = start.width + 'px';
      strip.style.height = start.height + 'px';
      $$('button', picker).forEach(function (button) {
        var icon = $('svg', button).cloneNode(true);
        if (button.getAttribute('aria-checked') === 'true') icon.classList.add('is-picked');
        strip.appendChild(icon);
      });
      document.body.appendChild(strip);
      flight = strip;
      var began = performance.now();
      function draw(now) {
        var t = Math.min(1, Math.max(0, (now - began) / 380));
        if (t === 1 || !picker.isConnected || !canFly()) { stopFlight(); return; }
        var rect = picker.getBoundingClientRect();
        var u = 1 - Math.pow(1 - t, 3), bow = Math.sin(t * Math.PI) * 20;
        var x = start.left + (rect.left - start.left + (rect.width - start.width) / 2) * u - bow;
        var y = start.top + (rect.top - start.top + (rect.height - start.height) / 2) * u;
        strip.style.transform = 'translate3d(' + x + 'px,' + y + 'px,0)';
        strip.style.opacity = String(Math.min(1, (1 - t) * 4));
        frame = requestAnimationFrame(draw);
      }
      draw(began);
    }
    function movePicker(toCard) {
      var destination = toCard ? dock : home;
      if (picker.parentElement === destination) return;
      var start = (flight || picker).getBoundingClientRect();
      stopFlight();
      if (toCard) {
        var size = home.getBoundingClientRect();
        home.style.setProperty(compactHeader.matches ? '--palette-compact' : '--palette-wide', size.width + 'px');
      }
      var focus = picker.contains(document.activeElement) && document.activeElement;
      if (destination.moveBefore) destination.moveBefore(picker, null);
      else {
        destination.appendChild(picker);
        if (focus) focus.focus({ preventScroll: true });
      }
      if (toCard) picker.setAttribute('aria-describedby', 'reference-hex');
      else picker.removeAttribute('aria-describedby');
      fly(start);
    }
    function observeCard() {
      var top = overview ? navigationHeight() : chapterTop(chapterHead(reference));
      var margin = '-' + top + 'px 0px 0px';
      function inView() {
        var rect = dock.getBoundingClientRect();
        var visible = Math.min(innerHeight, rect.bottom) - Math.max(top, rect.top);
        return rect.height > 0 && visible > rect.height * (picker.parentElement === dock ? .05 : .3);
      }
      movePicker(inView());
      if ('IntersectionObserver' in window && margin !== observedMargin) {
        if (observer) observer.disconnect();
        observedMargin = margin;
        observer = new IntersectionObserver(function () {
          if (dock.isConnected) movePicker(inView());
          else observer.disconnect();
        }, { rootMargin: margin, threshold: [0, .05, .3, 1] });
        observer.observe(dock);
      }
    }
    var requested = 0;
    function update(event) {
      var look = document.documentElement.dataset.tint;
      var choice = choices[look], revision = ++requested;
      if (!choice) return;
      function show() {
        if (event && picker.parentElement === dock) $('.cmp', section).dispatchEvent(new Event('studio:match-picked'));
      }
      if (reference.dataset.referenceLook === look) { show(); return; }
      preload(choice.file).then(function (ready) {
        if (requested !== revision || !reference.isConnected) return;
        if (!ready) { delete sources[choice.file]; return; }
        image.src = asset(choice.file);
        reference.dataset.referenceLook = look;
        reference.style.setProperty('--reference-colour', choice.colour);
        hex.textContent = choice.colour;
        name.setAttribute('data-i18n', 'look.' + look);
        name.textContent = text('look.' + look, look);
        show();
      });
    }
    window.addEventListener('ats-tint', update);
    window.addEventListener('resize', observeCard, { passive: true });
    document.addEventListener('ats:lang', observeCard);
    document.addEventListener('ats:leave', function () { if (observer) observer.disconnect(); stopFlight(); });
    if (!('IntersectionObserver' in window)) window.addEventListener('scroll', observeCard, { passive: true });
    subscribers.push(observeCard);
    observeCard();
    update();
  }

  function initNativeMotion(materialStudy) {
    var studies = {
      select: { frames: 24, files: ['select-0', 'select-1'], local: true, stops: [7, 11, 17, 19, 23], steps: 5, initial: 2,
        detail: ['select-detail-0', 'select-detail-1', 'select-detail-2', 'select-detail-3'],
        mobile: [
          { name: 'model', key: 'studio.select.model.view' },
          { name: 'uv', key: 'studio.select.texture.view' }
        ] },
      paint: { frames: 24, files: ['paint-0', 'paint-1'], local: true, recording: true, steps: 3, initial: 23,
        mobile: [{ name: 'model', key: 'studio.select.model.view' }] },
      material: { frames: 108, files: ['material-tint', 'material-satin', 'material-metal', 'material-shadow', 'material-matcap',
        'material-glitter', 'material-rim', 'material-outline', 'material-emission'],
        stops: [0, 11, 23, 35, 47, 59, 71, 83, 95, 107], steps: 10, initial: 107 }
    };
    if (materialStudy && materialStudy.schema === 1 && materialStudy.framesPerEffect === 12 &&
        Array.isArray(materialStudy.effects) && materialStudy.effects.length && materialStudy.effects.every(function (effect) {
          return /^material-[a-z-]+$/.test(effect.file) && typeof effect.title === 'string' && typeof effect.caption === 'string';
        })) {
      var effects = materialStudy.effects;
      studies.material = { frames: effects.length * 12, files: effects.map(function (effect) { return effect.file; }),
        stops: [0].concat(effects.map(function (effect, index) { return index * 12 + 11; })), steps: effects.length + 1,
        initial: effects.length * 12 - 1 };
      var materialButtons = $$('[data-motion-study="material"] button[data-image]', page);
      effects.forEach(function (effect, index) {
        var button = materialButtons[index + 1];
        if (!button) return;
        button.dataset.image = effect.image; button.dataset.caption = effect.caption;
        button.setAttribute('data-i18n', effect.title); button.textContent = text(effect.title);
      });
    }
    $$('[data-motion-study]', page).forEach(function (track) {
      var spec = studies[track.dataset.motionStudy];
      var stage = $('.studio-motion-stage', track);
      var canvas = $('[data-motion-canvas]', track);
      var context = canvas.getContext('2d');
      var buttons = $$('button[data-image]', track);
      var caption = $('[data-studio-caption]', track);
      var mobileSize = matchMedia('(max-width: 500px)');
      var mobile = null, mobileViews = [], mobileReady = false, layoutRequest = 0;
      if (spec.mobile) {
        mobile = make('div', 'studio-motion-mobile');
        spec.mobile.forEach(function (view) {
          var figure = make('figure');
          var detail = make('canvas');
          detail.width = detail.height = 1;
          detail.setAttribute('aria-hidden', 'true');
          var label = make('figcaption');
          label.setAttribute('data-i18n', view.key);
          label.textContent = text(view.key);
          figure.appendChild(detail); figure.appendChild(label); mobile.appendChild(figure);
          mobileViews.push({ name: view.name, canvas: detail, context: detail.getContext('2d') });
        });
        canvas.parentElement.insertAdjacentElement('afterend', mobile);
      }
      var progress = spec.initial / (spec.frames - 1), nearby = false;
      var raf = 0, sheets = {}, generation = 0;
      var files = spec.files, columns = 4, cells = 12, tileWidth = 800, tileHeight = 450;
      var detailFailed = false;
      var video = null, recordingKey = '', recordingInstalled = '', recordingUrl = '', recordingRequest = 0;
      var recordingFailed = false, recordingProgress = null;
      if (spec.recording) {
        video = make('video', 'studio-recording-source');
        video.muted = true;
        video.playsInline = true;
        video.preload = 'auto';
        video.setAttribute('aria-hidden', 'true');
        video.tabIndex = -1;
        canvas.parentElement.appendChild(video);
        recordingProgress = make('div', 'studio-recording-progress');
        recordingProgress.setAttribute('aria-hidden', 'true');
        stage.appendChild(recordingProgress);
        video.addEventListener('loadeddata', schedule);
        video.addEventListener('seeked', function () {
          if (!nearby) return;
          paintRecording();
          schedule();
        });
        video.addEventListener('error', function () {
          if (recordingInstalled !== recordingKey || video.currentSrc !== recordingUrl) return;
          recordingFailed = true; schedule();
        });
        window.addEventListener('pagehide', function (event) { if (!event.persisted && recordingUrl) URL.revokeObjectURL(recordingUrl); });
      }
      function localePath() {
        var lang = window.ATS ? window.ATS.lang() : document.documentElement.lang;
        return spec.local && lang && lang !== 'en' ? lang + '/' : '';
      }
      function file(index) {
        return localePath() + 'studio/motion/' + files[index] + '.webp';
      }
      function chooseSheets() {
        if (!spec.detail) return;
        var ratio = window.devicePixelRatio || 1;
        var detail = canvas.clientWidth * ratio > 800;
        if (mobileReady && mobileSize.matches) detail = mobileViews.some(function (view) {
          return view.canvas.clientWidth * ratio > view.rect[2];
        });
        if (detailFailed || (navigator.connection && navigator.connection.saveData)) detail = false;
        var next = detail ? spec.detail : spec.files;
        if (next === files) return;
        files = next;
        columns = detail ? 3 : 4;
        cells = detail ? 6 : 12;
        tileWidth = detail ? 1280 : 800;
        tileHeight = detail ? 720 : 450;
        sheets = {}; generation++;
      }
      function loadLayout() {
        if (!mobile) return;
        var request = ++layoutRequest;
        mobileReady = false; mobile.classList.remove('is-ready');
        loadJson(localePath() + 'studio/media-layout.json').then(function (layout) {
          if (request !== layoutRequest || layout.schema !== 1) return;
          var study = layout[track.dataset.motionStudy];
          if (!study || !mobileViews.every(function (view) {
            var rect = study[view.name];
            return Array.isArray(rect) && rect.length === 4 && rect.every(Number.isFinite) &&
              rect[0] >= 0 && rect[1] >= 0 && rect[2] > 0 && rect[3] > 0 && rect[0] + rect[2] <= 800 && rect[1] + rect[3] <= 450;
          })) return;
          mobileViews.forEach(function (view) {
            view.rect = study[view.name]; view.canvas.width = view.rect[2]; view.canvas.height = view.rect[3];
          });
          mobileReady = true; measure();
        }).catch(function () {  });
      }
      function frameSize(width, height) {
        if (canvas.width !== width || canvas.height !== height) { canvas.width = width; canvas.height = height; }
        if (mobileReady && mobileSize.matches) mobileViews.forEach(function (view) {
          var w = Math.round(view.rect[2] * width / 800), h = Math.round(view.rect[3] * height / 450);
          if (view.canvas.width !== w || view.canvas.height !== h) { view.canvas.width = w; view.canvas.height = h; }
        });
      }
      function drawFrame(source, x, y, width, height, alpha) {
        context.globalAlpha = alpha;
        context.drawImage(source, x, y, width, height, 0, 0, canvas.width, canvas.height);
        if (mobileReady && mobileSize.matches) mobileViews.forEach(function (view) {
          var rect = view.rect, scale = width / 800;
          view.context.globalAlpha = alpha;
          view.context.drawImage(source, x + rect[0] * scale, y + rect[1] * scale, rect[2] * scale, rect[3] * scale, 0, 0, view.canvas.width, view.canvas.height);
        });
      }
      function paintRecording() {
        if (overview || recordingInstalled !== recordingKey || video.currentSrc !== recordingUrl || video.readyState < 2 || video.seeking) return false;
        frameSize(video.videoWidth, video.videoHeight);
        drawFrame(video, 0, 0, video.videoWidth, video.videoHeight, 1);
        canvas.classList.add('is-ready');
        if (mobileReady && mobile && mobileSize.matches) mobile.classList.add('is-ready');
        var actual = video.currentTime / Math.max(.001, video.duration - .05);
        recordingProgress.style.setProperty('--recording-progress', Math.min(1, actual).toFixed(4));
        track.dataset.recordingTime = video.currentTime.toFixed(3);
        track.dataset.motionMedium = 'recording';
        track.dataset.motionFrame = String(Math.round(actual * (spec.frames - 1)));
        return true;
      }
      function seekRecording() {
        if (!video || recordingFailed || overview) return false;
        var key = localePath() + track.dataset.recording;
        if (key !== recordingKey) {
          recordingKey = key;
          recordingInstalled = '';
          var request = ++recordingRequest;
          track.dataset.recordingSource = key;
          fetch(asset(key)).then(function (response) {
            if (!response.ok) throw Error(response.status);
            return response.blob();
          }).then(function (blob) {
            if (request !== recordingRequest) return;
            if (recordingUrl) URL.revokeObjectURL(recordingUrl);
            recordingUrl = URL.createObjectURL(blob);
            recordingInstalled = key;
            video.src = recordingUrl;
            video.load();
          }).catch(function () {
            if (request !== recordingRequest) return;
            recordingFailed = true; schedule();
          });
        }
        if (recordingInstalled !== key) return false;
        if (!isFinite(video.duration) || video.readyState < 2) return false;
        var target = progress * Math.max(0, video.duration - .05);
        if (Math.abs(target - video.currentTime) > .035) {
          if (!video.seeking) video.currentTime = target;
          return true;
        }
        paintRecording();
        return true;
      }
      function sheet(index) {
        var key = file(index);
        if (!sheets[key]) {
          var entry = sheets[key] = { image: new Image(), ready: false };
          var request = generation;
          entry.image.onload = function () {
            if (request !== generation) return;
            entry.ready = true;
            schedule();
          };
          if (files === spec.detail) entry.image.onerror = function () {
            if (request !== generation) return;
            detailFailed = true; schedule();
          };
          entry.image.src = asset(key);
        }
        return sheets[key];
      }
      function retainNearbySheets(index) {
        var keep = [index - 1, index, index + 1].filter(function (at) { return at >= 0 && at < files.length; }).map(file);
        Object.keys(sheets).forEach(function (key) {
          if (keep.indexOf(key) === -1) delete sheets[key];
        });
      }
      function tile(index, alpha) {
        var image = sheet(Math.floor(index / cells)).image;
        var cell = index % cells;
        drawFrame(image, (cell % columns) * tileWidth, Math.floor(cell / columns) * tileHeight, tileWidth, tileHeight, alpha);
      }
      function syncButtons(frame) {
        if (!buttons.length) return;
        var index;
        if (spec === studies.select) index = frame < 8 ? 0 : frame < 12 ? 1 : frame < 18 ? 2 : frame < 20 ? 3 : 4;
        else index = frame < 1 ? 0 : Math.min(buttons.length - 1, Math.floor(frame / 12) + 1);
        buttons.forEach(function (button, i) { button.setAttribute('aria-pressed', String(i === index)); });
        var key = buttons[index].dataset.caption;
        if (caption && caption.getAttribute('data-i18n') !== key) {
          caption.setAttribute('data-i18n', key);
          caption.textContent = text(key);
          arrive(caption);
        }
      }
      function draw() {
        nearby = studyNearby(track);
        if (!nearby) return;
        if (seekRecording()) return;
        chooseSheets();
        var position = progress * (spec.frames - 1);
        var first = spec.local ? Math.round(position) : Math.floor(position);
        var second = spec.local ? first : Math.min(first + 1, spec.frames - 1);
        var group = Math.floor(first / cells);
        retainNearbySheets(group);
        if (!sheet(group).ready || !sheet(Math.floor(second / cells)).ready) return;
        frameSize(tileWidth, tileHeight);
        context.clearRect(0, 0, canvas.width, canvas.height);
        if (mobileSize.matches) mobileViews.forEach(function (view) { view.context.clearRect(0, 0, view.canvas.width, view.canvas.height); });
        context.globalCompositeOperation = 'source-over';
        mobileViews.forEach(function (view) { view.context.globalCompositeOperation = 'source-over'; });
        tile(first, second !== first ? 1 - (position - first) : 1);
        if (second !== first) {
          context.globalCompositeOperation = 'lighter';
          mobileViews.forEach(function (view) { view.context.globalCompositeOperation = 'lighter'; });
          tile(second, position - first);
        }
        context.globalCompositeOperation = 'source-over';
        mobileViews.forEach(function (view) { view.context.globalCompositeOperation = 'source-over'; view.context.globalAlpha = 1; });
        context.globalAlpha = 1;
        canvas.classList.add('is-ready');
        if (mobileReady && mobile && mobileSize.matches) mobile.classList.add('is-ready');
        track.dataset.motionFrame = String(first);
        track.dataset.motionFile = file(group);
        if (recordingProgress) recordingProgress.style.setProperty('--recording-progress', progress.toFixed(4));
        syncButtons(first);
      }
      function render() {
        raf = 0;
        draw();
      }
      function schedule() {
        nearby = studyNearby(track);
        if (!raf && nearby) raf = requestAnimationFrame(render);
      }
      if (spec.recording) {
        var note = $('.studio-tools-note', track.closest('.studio-section'));
        if (note) stage.appendChild(note);
      }
      var journey = scrollStudy(track, function (value) { progress = value; draw(); }, spec.steps, progress);
      function measure() {
        reserveCaption(track, buttons);
        journey.measure(); schedule();
      }
      if (document.fonts) document.fonts.ready.then(measure);
      buttons.forEach(function (button, index) {
        button.addEventListener('click', function () { journey.jump(spec.stops[index] / (spec.frames - 1)); });
      });
      arrowKeys(buttons);
      window.addEventListener('resize', measure, { passive: true });
      document.addEventListener('ats:lang', function () { sheets = {}; generation++; recordingFailed = detailFailed = false; loadLayout(); measure(); });
      subscribers.push(measure);
      if ('IntersectionObserver' in window) {
        pageObserver(IntersectionObserver, function (entries) {
          nearby = entries[0].isIntersecting || studyNearby(track);
          if (nearby) schedule();
          else { sheets = {}; generation++; }
        }, { rootMargin: '400px 0px' }).observe(track);
      } else nearby = true;
      loadLayout(); measure();
    });
  }

  function holdStudy(root, track, stage, distance, onMode, draw) {
    var frame = 0, top = 0, height = 380, pinned = false;
    var run = 1, tail = 0;
    var pace = paceTrack(track, measure);
    var head = chapterHead(root);
    var held = make('div', 'studio-held');
    track.insertBefore(held, stage);
    held.appendChild(stage);
    var nextTitle = nextChapter(head, held);
    function render() {
      frame = 0;
      if (!pinned) return;
      var rect = track.getBoundingClientRect();
      var travelled = top - rect.top;
      var progress = Math.max(0, Math.min(1, travelled / run));
      showNextChapter(nextTitle, tail ? travelled >= run + tail * .7 : progress >= .88);
      draw(progress);
    }
    function schedule() { if (!frame && root.isConnected) frame = requestAnimationFrame(render); }
    function measure() {
      top = chapterTop(head);
      var available = window.innerHeight - top - 12;
      var next = !overview && available >= 365;
      if (pinned !== next || root.dataset.state === 'idle') onMode(next);
      pinned = next;
      root.toggleAttribute('data-pin', pinned);
      root.style.setProperty('--pin-top', top + 'px');
      var nextSpace = phoneChapters.matches && pinned && nextTitle ? nextTitle.offsetHeight + 8 : 0;
      var artHeight = Math.min(860, available - nextSpace);
      height = artHeight + nextSpace;
      root.style.setProperty('--stage-h', artHeight + 'px');
      held.style.setProperty('--held-h', height + 'px');
      run = Math.max(1, Math.round(distance(window.innerHeight))) / scrollSpeed;
      tail = track.hasAttribute('data-hold-result') ? resultHold(artHeight) / scrollSpeed : 0;
      pace.top = top;
      pace.travel = pinned ? run + tail : 0;
      track.style.height = pinned ? Math.round(height + run + tail) + 'px' : '';
      stage.tabIndex = pinned ? 0 : -1;
      schedule();
    }
    window.addEventListener('resize', measure, { passive: true });
    document.addEventListener('ats:lang', measure);
    subscribers.push(measure);
    if (head && 'ResizeObserver' in window) pageObserver(ResizeObserver, measure).observe(head);
    if ('IntersectionObserver' in window) {
      pageObserver(IntersectionObserver, function (entries) {
        if (entries[entries.length - 1].isIntersecting) {
          window.addEventListener('scroll', schedule, { passive: true });
          schedule();
        } else window.removeEventListener('scroll', schedule);
      }, { rootMargin: '300px 0px' }).observe(track);
    } else window.addEventListener('scroll', schedule, { passive: true });
    measure();
    root.dataset.state = 'ready';
    return function (fraction) {
      if (!pinned) return;
      var rect = track.getBoundingClientRect();
      window.scrollTo({ top: Math.round(window.scrollY + rect.top - top + fraction * run) + 1, behavior: 'instant' });
      schedule();
    };
  }

  function studyStrip(items, position, active) {
    items.forEach(function (item, index) {
      var offset = index - position, distance = Math.abs(offset);
      item.style.setProperty('--d', offset.toFixed(3));
      item.style.setProperty('--s', (1 + .45 * Math.max(0, 1 - distance) - Math.min(distance, 3) * .05).toFixed(3));
      item.style.opacity = Math.max(0, 1 - distance * .38).toFixed(3);
      item.classList.toggle('is-near', index === active);
    });
  }

  function studyCount(node, index, total) {
    if (node.dataset.count === index + '/' + total) return;
    node.dataset.count = index + '/' + total;
    node.textContent = index ? String(index).padStart(2, '0') : '';
    if (index) {
      var suffix = make('small');
      suffix.textContent = '/ ' + total;
      node.appendChild(suffix);
    }
  }

  function studyKeys(stage, current, total, pick) {
    stage.addEventListener('keydown', function (event) {
      var key = event.key;
      var delta = key === 'ArrowDown' || key === 'ArrowRight' ? 1 : key === 'ArrowUp' || key === 'ArrowLeft' ? -1 : 0;
      if (!delta && key !== 'Home' && key !== 'End') return;
      var last = (typeof total === 'function' ? total() : total) - 1;
      var index = key === 'Home' ? 0 : key === 'End' ? last : Math.max(0, Math.min(last, current() + delta));
      event.preventDefault();
      pick(index);
    });
  }

  function initStack() {
    var root = $('#stack');
    var track = $('#stack-track', root);
    var stage = $('.stack-stage', root);
    var scene = $('#stack-scene', root);
    var labels = $('#stack-labels', root);
    var count = $('#stack-count', root);
    var cards = $('#stack-cards', root);

    var controls = $('.stack-materials', root);
    var buttons = $$('button', controls);

    loadJson('studio/layer-stack.json').then(function (data) {
      var materials = Object.assign({ earcup: data.layers.filter(function (row) {
        return row.id !== '07-finish';
      }) }, data.materials);
      var rows, pile, sheets, names, tiles, active, maskAt, maskedLayer, levels;
      var progress = 0;

      function imageFor(file) {
        var img = make('img');
        img.alt = '';
        img.width = img.height = 480;
        img.loading = 'lazy';
        img.decoding = 'async';
        img.src = asset(studioPath(file));
        return img;
      }

      function translate() {
        rows.forEach(function (row, index) {
          var name = row.composite ? text('studio.layers.combined') : text('studio.layer.' + row.id.replace(/^\d+-/, ''), row.id);
          var setting;
          if (!index) setting = text('mode.kind.o');
          else if (row.composite) setting = text('studio.layer.combined.note');
          else if (row.maskOf) setting = text('studio.layer.mask.setting');
          else setting = text('mode.b.' + row.blend.toLowerCase(), row.blend) + ' · ' + row.opacity + '%';
          tiles[index].title.textContent = names[index].firstElementChild.textContent = name;
          tiles[index].detail.textContent = names[index].lastElementChild.textContent = setting;
        });
      }

      function choose(material) {
        var layers = materials[material];
        if (!Array.isArray(layers) || !layers.length || root.dataset.material === material) return;
        root.dataset.material = material;
        buttons.forEach(function (button) {
          button.setAttribute('aria-pressed', String(button.dataset.stackMaterial === material));
        });
        rows = layers.concat({ composite: true, swatch: layers[layers.length - 1].swatch });
        active = -1;
        names = []; tiles = []; levels = [];
        labels.replaceChildren(); cards.replaceChildren();
        pile = make('div', 'pile');
        maskAt = layers.findIndex(function (row) { return row.maskOf; });
        maskedLayer = maskAt < 0 ? -1 : layers.findIndex(function (row) { return row.id === layers[maskAt].maskOf; });
        var depth = 0;
        sheets = rows.map(function (row, index) {
          if (index && !row.maskOf && !row.composite) depth++;
          levels.push(depth + (row.maskOf ? .16 : 0));
          var sheet = make('div', 'sl' + (!index ? ' sl--base' : '') + (row.composite ? ' sl--composite' : ''));
          var img = imageFor(row.plane || row.swatch);
          sheet.appendChild(img);
          sheet.style.setProperty('--i', row.composite ? 0 : levels[index]);
          pile.appendChild(sheet);
          var name = make('li');
          name.append(make('span'), make('small'));
          labels.appendChild(name);
          names.push(name);
          var tile = make('li', 'stack-card' + (!index ? ' is-base' : ''));
          var preview = make('div', 'stack-card-pic');
          var number = make('span', 'stack-card-n tnum');
          number.textContent = index + 1;
          preview.append(imageFor(row.swatch), number);
          var title = make('b', 'stack-card-t');
          var detail = make('span', 'stack-card-m');
          tiles.push({ title: title, detail: detail });
          tile.append(preview, title, detail);
          cards.appendChild(tile);
          return sheet;
        });
        scene.replaceChildren(pile);
        translate();
        update(progress);
      }

      function update(value) {
        progress = value;
        var final = rows.length - 1;
        var position = Math.min(1, Math.max(0, (progress - .04) / .88)) * final;
        var index = Math.round(position);
        var fold = Math.max(0, position - (final - 1));
        fold = fold * fold * (3 - 2 * fold);
        var h = stage.clientHeight, w = stage.clientWidth;
        var wide = w >= 900;
        pile.style.setProperty('--lw', Math.round(wide ? Math.min(w * .35, h * .63) : Math.min(w * .69, h * .44)) + 'px');
        var gap = Math.round(Math.min(wide ? 66 : 44, h * (wide ? .47 : .34) / Math.max(1, levels[final])));
        var depthAt = Math.floor(position), nextDepth = Math.min(depthAt + 1, final);
        var depth = levels[depthAt] + (levels[nextDepth] - levels[depthAt]) * (position - depthAt);
        pile.style.setProperty('--gap', gap + 'px');
        pile.style.setProperty('--rise', (.39 * gap * depth * (1 - fold)).toFixed(1) + 'px');
        pile.style.setProperty('--e', (1 - fold).toFixed(4));
        sheets.forEach(function (sheet, at) {
          var enter = Math.max(0, Math.min(1, position - at + 1));
          var opacity = at === final ? fold : (1 - fold) * enter;
          if (at === maskedLayer) opacity *= 1 - .72 * Math.max(0, Math.min(1, position - maskAt + 1));
          sheet.style.setProperty('--visible', opacity.toFixed(3));
          sheet.classList.toggle('is-focus', at === index);
        });
        studyStrip(names, position, index);
        if (active !== index) {
          if (active >= 0) names[active].removeAttribute('aria-current');
          names[index].setAttribute('aria-current', 'step');
          active = index;
          studyCount(count, index + 1, rows.length);
        }
      }
      choose('earcup');
      buttons.forEach(function (button) {
        button.hidden = !materials[button.dataset.stackMaterial];
        button.addEventListener('click', function () { choose(button.dataset.stackMaterial); });
      });
      var availableButtons = buttons.filter(function (button) { return !button.hidden; });
      controls.hidden = availableButtons.length < 2;
      arrowKeys(availableButtons);
      document.addEventListener('ats:lang', translate);
      var jump = holdStudy(root, track, stage, function (height) { return height * (1 + materials.earcup.length * .42); }, function (pin) {
        track.hidden = !pin;
        cards.hidden = pin;
      }, update);
      studyKeys(stage, function () { return active; }, function () { return rows.length; }, function (index) {
        jump(.04 + .88 * index / (rows.length - 1));
      });
    }).catch(function () {
    });
  }

  function initBlends() {
    var root = $('#modes-root');
    var track = $('#modes-track', root);
    var stage = $('.modes-stage', root);
    var view = $('#modes-view', root);
    var list = $('#modes-list', root);
    var grid = $('#modes-grid', root);
    var name = $('#modes-name', root);
    var kind = $('#modes-kind', root);
    var count = $('#modes-count', root);

    loadJson('studio/blends.json').then(function (data) {
      if (!data.base || !data.blend.length) return;
      var families = {
        multiply: 'darken', darken: 'darken', 'color-burn': 'darken', 'linear-burn': 'darken', 'darker-color': 'darken',
        screen: 'lighten', add: 'lighten', lighten: 'lighten', 'color-dodge': 'lighten', 'glow-dodge': 'lighten', 'add-glow': 'lighten', 'lighter-color': 'lighten',
        overlay: 'contrast', 'soft-light': 'contrast', 'hard-light': 'contrast', 'vivid-light': 'contrast', 'linear-light': 'contrast', 'pin-light': 'contrast', 'hard-mix': 'contrast',
        subtract: 'compare', difference: 'compare', exclusion: 'compare', divide: 'compare',
        color: 'colour', hue: 'colour', saturation: 'colour', luminosity: 'colour'
      };
      var rows = [{ type: 'o', id: 'base', img: data.base }];
      data.blend.forEach(function (row) { rows.push({ type: 'b', id: row.id, img: row.img }); });
      data.adjust.forEach(function (row) { rows.push({ type: 'a', id: row.id, img: row.img }); });
      var order = ['', 'darken', 'lighten', 'contrast', 'compare', 'colour'];
      var countB = data.blend.length, countA = data.adjust.length;
      var active = -1, target = -1, request = 0, position = 0;
      var currentImage = null;
      var items = rows.map(function () {
        var item = make('li');
        list.appendChild(item);
        return item;
      });
      var captions = [];

      function effect(row) {
        return row.type === 'o' ? text('mode.base', 'Before') : text('mode.' + row.type + '.' + row.id, row.id.replace(/-/g, ' '));
      }
      function family(row) { return row.type === 'b' ? families[row.id] || '' : ''; }
      function group(type) {
        return text('mode.kind.' + type).replace('{n}', type === 'b' ? countB : countA);
      }
      function heading(node, key) {
        captions.push(function () { node.textContent = key(); });
        grid.appendChild(node);
      }
      function addCard(row) {
        var card = make('figure', 'modes-card');
        var image = make('img');
        image.src = asset(studioPath(row.img));
        image.alt = '';
        image.width = 960;
        image.height = 960;
        image.loading = 'lazy';
        image.decoding = 'async';
        card.appendChild(image);
        var caption = make('figcaption');
        captions.push(function () { caption.textContent = effect(row); });
        card.appendChild(caption);
        grid.appendChild(card);
      }
      heading(make('h3', 'modes-gh'), function () { return group('b'); });
      order.forEach(function (id) {
        if (id) heading(make('p', 'modes-sub'), function () { return text('mode.g.' + id); });
        rows.filter(function (row) { return row.type !== 'a' && family(row) === id; }).forEach(addCard);
      });
      if (countA) {
        heading(make('h3', 'modes-gh'), function () { return group('a'); });
        rows.filter(function (row) { return row.type === 'a'; }).forEach(addCard);
      }

      function updateCaption() {
        if (active < 0) return;
        var row = rows[active];
        name.textContent = effect(row);
        kind.textContent = group(row.type);
        studyCount(count, row.type === 'o' ? 0 : row.type === 'b' ? active : active - countB, row.type === 'a' ? countA : countB);
      }
      function translate() {
        items.forEach(function (item, index) {
          var row = rows[index];
          item.textContent = effect(row);
          var groupName = family(row);
          if (groupName) {
            var category = make('small');
            category.textContent = text('mode.g.' + groupName);
            item.appendChild(category);
          }
        });
        captions.forEach(function (apply) { apply(); });
        updateCaption();
      }
      function alignNames() {
        if (active >= 0) studyStrip(items, active === target ? position : active, active);
      }
      function update(progress) {
        position = Math.max(0, Math.min(1, (progress - .02) / .94)) * (rows.length - 1);
        var index = Math.round(position);
        if (index === target) { alignNames(); return; }
        target = index;
        var run = ++request;
        if (index === active) { alignNames(); return; }
        var next = make('img', 'is-on');
        next.alt = '';
        next.width = next.height = 960;
        next.decoding = 'async';
        next.onload = function () {
          if (run !== request) return;
          var decoded = next.decode ? next.decode() : Promise.resolve();
          decoded.then(function () {
            if (run !== request) return;
            if (currentImage) view.replaceChild(next, currentImage);
            else view.insertBefore(next, name);
            currentImage = next;
            active = index;
            alignNames();
            updateCaption();
          }, function () {});
        };
        next.src = asset(studioPath(rows[index].img));
        if (rows[index + 1]) preload(studioPath(rows[index + 1].img));
      }
      translate();
      document.addEventListener('ats:lang', translate);
      var jump = holdStudy(root, track, stage, function (height) { return rows.length * height * .11; }, function (pin) {
        track.hidden = !pin;
        grid.hidden = pin;
      }, update);
      studyKeys(stage, function () { return target; }, rows.length, function (index) {
        jump(.02 + index / (rows.length - 1) * .94);
      });
    }).catch(function () {
    });
  }

  near($('#stack'), initStack);
  near($('#modes-root'), initBlends);
  loadJson('studio/material-study.json').then(initNativeMotion, function () { initNativeMotion(null); });
  initScrollComparisons();
  near($('#colour'), initColourMatch);
  initOpening();
  var linking = false, linkFrame = 0;
  function alignLink() {
    linkFrame = 0;
    if (!linking) return;
    var id = location.hash.slice(1);
    var target = document.getElementById(previousLinks[id] || id);
    if (!target || !target.getClientRects().length) return;
    if (target.tagName === 'DETAILS') target.open = true;
    var offset = pinTop();
    if (!overview && !target.matches('.studio-section, .studio-map-example, .studio-alias, #stack, #modes-root')) {
      offset = chapterTop(chapterHead(target));
    }
    window.scrollTo({ top: Math.max(0, window.scrollY + target.getBoundingClientRect().top - offset), behavior: 'instant' });
  }
  function scheduleLink() {
    if (!linkFrame && linking) linkFrame = requestAnimationFrame(alignLink);
  }
  function settleLink() {
    linking = !!location.hash;
    scheduleLink();
    if (document.fonts) document.fonts.ready.then(scheduleLink);
  }
  ['wheel', 'touchstart', 'pointerdown', 'keydown'].forEach(function (name) {
    window.addEventListener(name, function () { linking = false; }, { passive: true });
  });
  if ('ResizeObserver' in window) pageObserver(ResizeObserver, scheduleLink).observe(flow);
  window.addEventListener('hashchange', settleLink);
  window.addEventListener('load', settleLink, { once: true });
  window.addEventListener('hashchange', followPreviousLink);
  window.addEventListener('load', followPreviousLink, { once: true });
}());
