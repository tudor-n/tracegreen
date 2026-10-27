import { memo } from 'react'

// The window backdrop from the design handoff: a muted landscape behind the glass panels. At rest it is desaturated,
// with smog and chimneys; once any supplier has verified (`clean`), it regains its colour over 1.8 s, the smog drifts
// off, the chimneys sink and wind turbines and trees grow in. Decoration only, hidden from screen readers; it never
// carries a number. Drawn from the board's SVG (vehicles left out: they belong to the simulator).

function BackdropView({ clean }: { clean: boolean }) {
  return (
    <svg
      className="backdrop-art"
      data-clean={clean}
      viewBox="630 225 660 390"
      preserveAspectRatio="xMidYMid slice"
      aria-hidden
      focusable={false}
    >
      <defs>
      <linearGradient id="dSkyC" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#79b2e2" /><stop offset=".5" stopColor="#c9e1f2" /><stop offset="1" stopColor="#eef5f2" /></linearGradient>
      <linearGradient id="dSkyD" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#7d7a75" /><stop offset=".5" stopColor="#b6aea2" /><stop offset="1" stopColor="#d8cfc1" /></linearGradient>
      <filter id="dSoft" x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="7" /></filter>
      <filter id="dPuff" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="2.4" /></filter>
      </defs>
      <rect width="1280" height="760" fill="url(#dSkyC)" />
      <rect className="skyd" width="1280" height="760" fill="url(#dSkyD)" />
      <g className="clouds"><g className="cloud"><rect x="700" y="262" width="130" height="30" rx="15" /><circle cx="746" cy="262" r="22" /><circle cx="782" cy="256" r="28" /></g><g className="cloud"><rect x="1120" y="268" width="120" height="28" rx="14" /><circle cx="1160" cy="268" r="20" /><circle cx="1194" cy="262" r="26" /></g></g>
      <g className="smog" filter="url(#dSoft)"><circle cx="760" cy="270" r="60" /><circle cx="900" cy="250" r="80" /><circle cx="1050" cy="270" r="80" /><circle cx="1180" cy="250" r="74" /></g>
      <path className="mtn" d="M590 336 L650 312 L720 348 L790 322 L850 356 L920 330 L990 366 L1060 340 L1130 380 L1200 362 L1280 392 L1300 400 V520 H590Z" />
      <path className="snow" d="M650 312L674 324L664 324L654 324L640 323L628 321ZM790 322L814 336L804 335L794 334L780 333L768 330ZM920 330L944 342L934 342L924 342L910 341L898 338ZM1060 340L1084 354L1074 353L1064 352L1050 351L1038 348Z" />
      <g transform="translate(860,500)"><g className="mill m2"><path className="tw" d="M-4.6 0L-2 -180H2L4.6 0Z" /><path className="tws" d="M0 0V-180H2L4.6 0Z" /><rect className="nac" x="-5" y="-184" width="13" height="8" rx="3" /><g transform="translate(0,-180)"><g className="blades"><circle className="guard" r="72" /><path className="bl" d="M0 0C4 -12 4.6 -41 0 -68C-2.6 -41 -3 -12 0 0Z" /><path className="bl" transform="rotate(120)" d="M0 0C4 -12 4.6 -41 0 -68C-2.6 -41 -3 -12 0 0Z" /><path className="bl" transform="rotate(240)" d="M0 0C4 -12 4.6 -41 0 -68C-2.6 -41 -3 -12 0 0Z" /></g><circle className="hub" r="4.2" /></g></g></g>
      <g transform="translate(1000,500)"><g className="mill m3"><path className="tw" d="M-4.6 0L-2 -200H2L4.6 0Z" /><path className="tws" d="M0 0V-200H2L4.6 0Z" /><rect className="nac" x="-5" y="-204" width="13" height="8" rx="3" /><g transform="translate(0,-200)"><g className="blades"><circle className="guard" r="80" /><path className="bl" d="M0 0C4 -14 4.6 -46 0 -76C-2.6 -46 -3 -14 0 0Z" /><path className="bl" transform="rotate(120)" d="M0 0C4 -14 4.6 -46 0 -76C-2.6 -46 -3 -14 0 0Z" /><path className="bl" transform="rotate(240)" d="M0 0C4 -14 4.6 -46 0 -76C-2.6 -46 -3 -14 0 0Z" /></g><circle className="hub" r="4.2" /></g></g></g>
      <g transform="translate(1232,500)"><g className="mill m4"><path className="tw" d="M-4.6 0L-2 -190H2L4.6 0Z" /><path className="tws" d="M0 0V-190H2L4.6 0Z" /><rect className="nac" x="-5" y="-194" width="13" height="8" rx="3" /><g transform="translate(0,-190)"><g className="blades"><circle className="guard" r="76" /><path className="bl" d="M0 0C4 -13 4.6 -43 0 -72C-2.6 -43 -3 -13 0 0Z" /><path className="bl" transform="rotate(120)" d="M0 0C4 -13 4.6 -43 0 -72C-2.6 -43 -3 -13 0 0Z" /><path className="bl" transform="rotate(240)" d="M0 0C4 -13 4.6 -43 0 -72C-2.6 -43 -3 -13 0 0Z" /></g><circle className="hub" r="4.2" /></g></g></g>
      <path className="hm" d="M600 482C620 470 630 452 640 452C687 453 737 475 780 476C823 477 857 457 900 456C943 455 993 470 1040 468C1087 466 1137 447 1180 446C1223 445 1280 459 1300 462V760H600Z" />
      <g className="smoke" filter="url(#dPuff)">
      <g transform="translate(1055,352)"><circle className="puff p1" r="15" /><circle className="puff p2" r="18" /><circle className="puff p3" r="14" /><circle className="puff p4" r="19" /><circle className="puff p5" r="15" /><circle className="puff p6" r="17" /></g>
      <g className="s2" transform="translate(1109,388)"><circle className="puff p1" r="12" /><circle className="puff p2" r="15" /><circle className="puff p3" r="11" /><circle className="puff p4" r="16" /><circle className="puff p5" r="12" /><circle className="puff p6" r="14" /></g>
      </g>
      <g className="chim"><rect className="brick" x="1044" y="356" width="22" height="250" /><rect className="cband" x="1044" y="366" width="22" height="6" /><rect className="cband" x="1044" y="380" width="22" height="4" /><rect className="ccap" x="1041" y="352" width="28" height="6" rx="1" /></g>
      <g className="chim"><rect className="brick" x="1100" y="392" width="18" height="214" /><rect className="cband" x="1100" y="402" width="18" height="5" /><rect className="ccap" x="1097" y="388" width="24" height="5" rx="1" /></g>
      <rect className="wallc" x="930" y="528" width="150" height="78" />
      <path className="roof" d="M930 528L960 512V528L990 512V528L1020 512V528L1050 512V528L1080 512V528Z" />
      <path className="glz" d="M960 513V528M990 513V528M1020 513V528M1050 513V528M1080 513V528" />
      <rect className="fw" x="942" y="546" width="16" height="22" rx="2" /><rect className="fw" x="970" y="546" width="16" height="22" rx="2" /><rect className="fw" x="998" y="546" width="16" height="22" rx="2" /><rect className="fw" x="1026" y="546" width="16" height="22" rx="2" />
      <rect className="door" x="1052" y="568" width="20" height="38" rx="2" />
      <rect className="offc" x="1080" y="550" width="56" height="56" /><rect className="roof" x="1077" y="545" width="62" height="6" rx="1" />
      <rect className="fw" x="1090" y="560" width="13" height="10" rx="1.5" /><rect className="fw" x="1113" y="560" width="13" height="10" rx="1.5" /><rect className="fw" x="1090" y="578" width="13" height="10" rx="1.5" /><rect className="fw" x="1113" y="578" width="13" height="10" rx="1.5" />
      <path className="hn" d="M600 584C655 584 710 568 760 570C810 572 860 588 900 592C940 596 967 596 1000 596C1033 596 1067 597 1100 594C1133 591 1167 581 1200 580C1233 579 1283 587 1300 588V760H600Z" />
      <g transform="translate(700,574)"><g className="tree t2"><rect className="trk" x="-2" y="-5" width="4" height="5" /><path className="cd" d="M0 -46L11 -24H-11ZM0 -37L15 -14H-15ZM0 -27L18 -4H-18Z" /><path className="cl" d="M0 -46L-11 -24H0ZM0 -37L-15 -14H0ZM0 -27L-18 -4H0Z" /></g></g>
      <g transform="translate(735,571)"><g className="tree t3"><rect className="trk" x="-2" y="-13" width="4" height="13" /><circle className="dd" cy="-25" r="14" /><path className="dl" d="M0 -39A14 14 0 0 0 0 -11Z" /></g></g>
      <g transform="translate(780,574)"><g className="tree base"><rect className="trk" x="-2" y="-4" width="4" height="4" /><path className="cd" d="M0 -40L10 -21H-10ZM0 -32L13 -12H-13ZM0 -23L16 -3H-16Z" /><path className="cl" d="M0 -40L-10 -21H0ZM0 -32L-13 -12H0ZM0 -23L-16 -3H0Z" /></g></g>
      <g transform="translate(880,592)"><g className="tree base"><rect className="trk" x="-2" y="-5" width="4" height="5" /><path className="cd" d="M0 -52L12 -27H-12ZM0 -42L17 -16H-17ZM0 -30L21 -4H-21Z" /><path className="cl" d="M0 -52L-12 -27H0ZM0 -42L-17 -16H0ZM0 -30L-21 -4H0Z" /></g></g>
      <g transform="translate(905,595)"><g className="tree t4"><rect className="trk" x="-2" y="-4" width="4" height="4" /><path className="cd" d="M0 -40L10 -21H-10ZM0 -32L13 -12H-13ZM0 -23L16 -3H-16Z" /><path className="cl" d="M0 -40L-10 -21H0ZM0 -32L-13 -12H0ZM0 -23L-16 -3H0Z" /></g></g>
      <g transform="translate(1160,588)"><g className="tree t5"><rect className="trk" x="-2" y="-14" width="4" height="14" /><circle className="dd" cy="-27" r="16" /><path className="dl" d="M0 -43A16 16 0 0 0 0 -11Z" /></g></g>
      <g transform="translate(1185,584)"><g className="tree t6"><rect className="trk" x="-2" y="-5" width="4" height="5" /><path className="cd" d="M0 -50L12 -26H-12ZM0 -40L16 -15H-16ZM0 -29L20 -4H-20Z" /><path className="cl" d="M0 -50L-12 -26H0ZM0 -40L-16 -15H0ZM0 -29L-20 -4H0Z" /></g></g>
      <g transform="translate(1250,585)"><g className="tree base"><rect className="trk" x="-2" y="-4" width="4" height="4" /><path className="cd" d="M0 -44L11 -23H-11ZM0 -35L14 -13H-14ZM0 -26L18 -4H-18Z" /><path className="cl" d="M0 -44L-11 -23H0ZM0 -35L-14 -13H0ZM0 -26L-18 -4H0Z" /></g></g>
    </svg>
  )
}

export const Backdrop = memo(BackdropView)
