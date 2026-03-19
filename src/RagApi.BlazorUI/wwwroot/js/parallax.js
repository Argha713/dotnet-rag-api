// Argha - 2026-03-20 - #59 - Parallax: tracks mouse position and drives orb transform via CSS vars
(function () {
  var rx = 0, ry = 0;
  document.addEventListener('mousemove', function (e) {
    rx = (e.clientX / window.innerWidth  - 0.5) * 28;
    ry = (e.clientY / window.innerHeight - 0.5) * 28;
    document.documentElement.style.setProperty('--orb-x', rx + 'px');
    document.documentElement.style.setProperty('--orb-y', ry + 'px');
  });
})();
