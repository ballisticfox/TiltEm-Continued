// The Mathematics of Axial Tilt in Kerbal Space Program
// Companion to REFERENCE_FRAME_DEFECTS.md and TiltEm/Frames/TiltEmFrames.cs
//
// Build:  typst compile Docs/TILT_MATHEMATICS.typ

#set page(
  paper: "a4",
  margin: (x: 2.4cm, y: 2.4cm),
  numbering: "1",
  number-align: center,
)
#set text(size: 10.5pt)
#set par(justify: true, leading: 0.68em)
#set heading(numbering: "1.1")
#show heading.where(level: 1): it => block(above: 1.6em, below: 0.9em, sticky: true)[#it]
#show heading.where(level: 2): it => block(above: 1.2em, below: 0.7em, sticky: true)[#it]
#set math.equation(numbering: "(1)")
#show link: it => text(fill: rgb("#1a4d80"))[#it]
#show raw.where(block: true): it => block(
  width: 100%,
  fill: rgb("#f6f6f4"),
  inset: 8pt,
  radius: 3pt,
  breakable: true,
  text(size: 8pt)[#it],
)

#let thm(kind, body) = block(
  width: 100%,
  breakable: true,
  inset: (left: 10pt, rest: 8pt),
  stroke: (left: 2pt + rgb("#4a6fa5")),
  fill: rgb("#f4f7fb"),
  above: 1.1em,
  below: 1.1em,
)[#strong(kind) #h(0.25em) #body]

#let proofbox(body) = block(
  width: 100%,
  breakable: true,
  inset: (left: 10pt, rest: 4pt),
  above: 0.8em,
  below: 1.2em,
)[#emph[Proof.] #body #h(1fr) $square.stroked$]

#let T = $bold(T)$
#let Zup = $bold(Z)$
#let Bf = $bold(B)$
#let Lf = $bold(L)$
#let Af = $bold(A)$
#let Rz(x) = $R_z (#x)$
#let Rx(x) = $R_x (#x)$
#let tr = $upright("T")$

#align(center)[
  #v(1.5cm)
  #text(size: 20pt, weight: "bold")[The Mathematics of Axial Tilt \ in Kerbal Space Program]

  #v(0.4cm)
  #text(size: 12pt)[Reference frames, the rotating-frame switch, and orbital elements]

  #v(0.8cm)
  #text(size: 10pt)[The reference-frame construction used by the Tilt'Em mod \ for Kerbal Space Program 1.12]
  #v(1.2cm)
]

#block(
  width: 100%,
  inset: 12pt,
  fill: rgb("#f4f7fb"),
  stroke: 0.5pt + rgb("#c8d4e2"),
  radius: 4pt,
)[
  *What this is.* Kerbal Space Program has no axial tilt: every planet's north pole
  points along the same world axis, and the game's internal machinery quietly
  depends on that in more places than you would guess. This document develops,
  from first principles, the mathematics needed to introduce a real obliquity
  without breaking the machinery, and explains why several obvious approaches
  fail.

  *Required background.* Matrix multiplication, orthogonal matrices, and the dot and
  cross products. Everything past that is introduced where it is first used: rotation
  groups, conjugation, Euler-angle parameterisations, floating-point conditioning, and
  what little KSP internals and celestial mechanics are needed. The prerequisites are
  light, but the argument is dense, and the whole is suited for upper-level
  undergraduate students.

  *On the listings.* Code quoted from KSP is decompiled from the 1.12.5 assemblies,
  so its local names are not always the originals; where the decompiler emitted
  placeholders, they have been replaced with the names KSP itself uses for the same
  quantity elsewhere. Code quoted from the mod is verbatim.
]

#v(0.6cm)
#outline(depth: 2, indent: 1.2em)
#pagebreak()

= Frames, and how KSP stores them

== A frame is a matrix <sec-frames>

Fix a reference basis for three-dimensional space. A *frame* is an ordered triple
of mutually perpendicular unit vectors $(bold(x), bold(y), bold(z))$, each written
out in that reference basis, and ordered so that $bold(x) times bold(y) = bold(z)$.

Stack the three as the columns of a matrix:

$ bold(M) = mat(bold(x), bold(y), bold(z)) in RR^(3 times 3). $

Because the columns are orthonormal, $bold(M)^tr bold(M) = bold(I)$, so

$ bold(M)^(-1) = bold(M)^tr, $ <eq-transpose>

and because the triple is right-handed, $det bold(M) = +1$. Matrices with these two
properties are exactly the *rotation matrices*, the group $"SO"(3)$. So "frame",
"orthonormal basis" and "rotation matrix" are three names for one object, and we
use them interchangeably. Note the practical upshot of @eq-transpose:
*inverting a frame costs nothing but a transpose.*

== Two ways to read the same matrix <sec-readings>

A rotation matrix can be read in two ways. They are easy to conflate, so it is
worth separating them explicitly.

/ Active: $bold(M)$ takes a vector and *moves* it. The vector $bold(v)$ becomes
  $bold(M) bold(v)$, somewhere else in the same space.

/ Passive: $bold(M)$ takes *coordinates* and *relabels* them. If a vector has
  coordinates $bold(c)$ with respect to the frame's own axes, then its coordinates
  in the reference basis are $bold(M) bold(c)$. Nothing moved; we changed our
  description.

Both readings are the same arithmetic. Which one is meant is a matter of intent,
and this document will always say which.

== KSP's `CelestialFrame`

KSP stores a frame as its three columns, exactly as above:

```csharp
public struct CelestialFrame
{
    public Vector3d X, Y, Z;

    public Vector3d LocalToWorld(Vector3d r)   // = M r
    {
        return r.x * X + r.y * Y + r.z * Z;
    }

    public Vector3d WorldToLocal(Vector3d r)   // = transpose(M) r
    {
        return new Vector3d(Dot(r, X), Dot(r, Y), Dot(r, Z));
    }
}
```

#thm[Warning.][
  The method names are the opposite of what a newcomer expects. Here *"World"*
  means *the basis the columns are written in*, and *"Local"* means *this frame's
  own axes*. So `WorldToLocal` is multiplication by $bold(M)^tr$, and it converts
  *out of* the basis the frame lives in and *into* the frame. We will see in
  @sec-what-moves that for the frame called `Planetarium.Zup`, "Local" is the
  coordinate system the game engine actually draws in.
]

== The two elementary rotations

#block(sticky: true)[We only ever need rotations about the reference $z$- and $x$-axes:]

$
Rz(theta) = mat(
  cos theta, -sin theta, 0;
  sin theta,  cos theta, 0;
  0, 0, 1
),
quad
Rx(theta) = mat(
  1, 0, 0;
  0, cos theta, -sin theta;
  0, sin theta,  cos theta
).
$ <eq-elementary>

Both are the ordinary right-handed active rotations: $Rz(theta)$ sends $bold(e)_1$ to
$(cos theta, sin theta, 0)$, turning $+x$ towards $+y$ for positive $theta$. KSP's own
generator is built from these two and nothing else, which Proposition 2.1 verifies.

Two facts used constantly:

$ Rz(alpha) Rz(beta) = Rz(alpha + beta), quad Rz(theta)^tr = Rz(-theta). $ <eq-zcompose>

That is, rotations about a *common* axis commute and simply add their angles.
Rotations about *different* axes do neither. Propositions 5.4, 5.5 and 6.1 each
turn on that asymmetry, so it is worth having in mind.

= KSP's celestial frames share one generator <sec-generator>

Every celestial frame that matters here --- planet orientations, orbit planes, the
planetarium itself --- is built through a single function. Both public entry points
below reduce to it, and every `PlanetaryFrame` call site in the stock assemblies goes
through one of those two.

```csharp
public static void SetFrame(double A, double B, double C,
                            ref CelestialFrame cf)
{
    cf.X = new Vector3d( cosA*cosC - sinA*cosB*sinC,
                         sinA*cosC + cosA*cosB*sinC,
                         sinB*sinC);
    cf.Y = new Vector3d(-cosA*sinC - sinA*cosB*cosC,
                        -sinA*sinC + cosA*cosB*cosC,
                         sinB*cosC);
    cf.Z = new Vector3d( sinA*sinB, -cosA*sinB, cosB);
}
```

That wall of trigonometry is not arbitrary.

#thm[Proposition 2.1.][
  `SetFrame`$(A, B, C)$ stores the columns of

  $ bold(S)(A, B, C) = Rz(A) Rx(B) Rz(C). $ <eq-zxz>
]

#proofbox[
  Multiply the right-hand side out. First,
  $
  Rx(B) Rz(C) = mat(
    cos C, -sin C, 0;
    cos B sin C, cos B cos C, -sin B;
    sin B sin C, sin B cos C, cos B
  ).
  $
  Left-multiplying by $Rz(A)$ mixes the first two rows in the usual way and leaves
  the third alone:
  $
  bold(S) = mat(
    cos A cos C - sin A cos B sin C, #h(0.4em) -cos A sin C - sin A cos B cos C, #h(0.4em) sin A sin B;
    sin A cos C + cos A cos B sin C, #h(0.4em) -sin A sin C + cos A cos B cos C, #h(0.4em) -cos A sin B;
    sin B sin C, sin B cos C, cos B
  ).
  $
  Reading off the three *columns* --- recall from @sec-frames that a frame is stored
  as its columns --- reproduces `cf.X`, `cf.Y` and `cf.Z` exactly.
]

So KSP's generator is a *ZXZ Euler angle* parameterisation, one of several in use
across celestial mechanics that differ in axis order and in sign. KSP commits to this
one everywhere, for planetary orientations and orbital elements alike, which is what
lets frames of the two kinds compose directly. Its third column will be needed
repeatedly, so we record it:

$ bold(S)(A,B,C) bold(e)_3 = vec(sin A sin B, -cos A sin B, cos B). $ <eq-zcol>

Note that the third column does not depend on $C$ at all: $C$ is a spin *about*
that column.

== The two public entry points

KSP exposes `SetFrame` through two thin wrappers that differ only by constant
offsets:

$
"OrbitalFrame"(Omega, i, omega) &= bold(S)(Omega, i, omega), \
"PlanetaryFrame"(alpha, delta, r) &= bold(S)(alpha - 90degree, #h(0.2em) delta - 90degree, #h(0.2em) r + 90degree).
$ <eq-wrappers>

Because they share a generator, *frames built by one compose directly with frames
built by the other*, a fact we exploit in @sec-elements to convert orbital
elements between reference planes.

== What the planetary offsets are for

#thm[Proposition 2.2.][
  Write $T = "PlanetaryFrame"(alpha, delta, 0)$. Then

  $ T bold(e)_3 = vec(cos delta cos alpha, cos delta sin alpha, sin delta), $ <eq-pole>

  which is the unit vector at right ascension $alpha$ and declination $delta$.
]

#proofbox[
  Substitute $A = alpha - 90degree$ and $B = delta - 90degree$ into @eq-zcol, using
  $sin(alpha - 90degree) = -cos alpha$, $cos(alpha - 90degree) = sin alpha$, and
  likewise for $delta$:
  $
  T bold(e)_3
  = vec((-cos alpha)(-cos delta), -(sin alpha)(-cos delta), sin delta)
  = vec(cos delta cos alpha, cos delta sin alpha, sin delta).
  $
]

This is why the offsets exist. `PlanetaryFrame` is a frame *named by where its
third axis points*, in the astronomers' convention: right ascension and declination
of the north pole. That is the same pole representation the IAU body-orientation
models use, so published pole data converts into this form directly.

#thm[Using real IAU data.][
  A full IAU model gives the pole as a function of time: the right ascension and the
  declination each carry a drift rate, and the prime meridian angle advances with the
  body's spin. $T$ holds one fixed orientation, with no rate term anywhere in it, so
  the time dependence has to be collapsed before the numbers reach the game. That is
  done once, offline, when the planet configuration is written: evaluate the IAU
  expressions at a chosen instant, the *epoch*, and record the resulting $alpha$,
  $delta$ and prime meridian as three constants. Every body in a system needs the
  same epoch, since a pole is only meaningful against the frame of the moment it was
  taken.
]

#thm[Proposition 2.3.][
  $"PlanetaryFrame"(alpha, delta, r) = T dot Rz(r)$, with $T$ as above.
]

#proofbox[
  From @eq-wrappers and @eq-zxz,
  $
  "PlanetaryFrame"(alpha, delta, r)
  &= Rz(alpha - 90degree) Rx(delta - 90degree) Rz(r + 90degree) \
  &= underbrace(Rz(alpha - 90degree) Rx(delta - 90degree) Rz(90degree), T) dot Rz(r),
  $
  where the last step splits $Rz(r + 90degree) = Rz(90degree) Rz(r)$ using
  @eq-zcompose.
]

So a planetary frame factors cleanly into *a constant part carrying the pole* and
*a variable part carrying the spin*. @sec-construction works entirely in terms of
that factorisation.

== The degenerate case, and why stock KSP is stuck <sec-degenerate>

#thm[Proposition 2.4.][
  $"PlanetaryFrame"(0, 90degree, r) = Rz(r)$, a pure spin about the reference
  $z$-axis.
]

#proofbox[
  Here $A = -90degree$, $B = 0$, $C = r + 90degree$. Since $Rx(0) = bold(I)$,
  $
  bold(S) = Rz(-90degree) Rz(r + 90degree) = Rz(-90degree + r + 90degree) = Rz(r).
  $
]

This has a consequence for the unmodified game:

#thm[Corollary 2.5.][
  Stock KSP calls `PlanetaryFrame` with $(alpha, delta) = (0, 90degree)$ and
  *nothing else*, anywhere. Consequently every body's pole is $bold(e)_3$, and
  obliquity is not merely absent: it is *structurally inexpressible* through the
  code path the game uses.
]

That is the constraint the whole mod is built around. We cannot bolt a tilt onto
the output of `PlanetaryFrame`$(0, 90, r)$, because by @eq-pole the information has
already been discarded. We must pass a real pole *into* the generator instead.

= What the game does with these frames <sec-what-moves>

Before adding tilt we need to know what the frames are actually consumed by. KSP
does something here that has no counterpart in most engines.

== The rotating frame

When the active vessel drops below a per-body altitude
(`inverseRotThresholdAltitude`, 100 km at Kerbin), KSP flips that body into
*inverse rotation*. Physically the motivation is straightforward. A vessel sitting
on the launch pad of a body that rotates once per 21 549 s at a radius of 600 km is
moving at about 175 m/s in world coordinates. The rotating frame exists to keep the
local physics simulation from having to carry that motion, so that contacts between a
landing leg and a 600 km collider mesh are resolved between two things at rest rather
than two things travelling at 175 m/s.

So KSP stops the planet and turns the universe instead.

== The two angles

Each body carries a spin phase that is pure physics, a function of time and
nothing else:

$ theta(t) = ("initialRotation" + 360degree dot t / P) mod 360degree, $ <eq-theta>

where $t$ is universal time and $P$ the body's rotation period, both in seconds, and
`initialRotation` is the body's spin phase in degrees at $t = 0$. $P$ and
`initialRotation` are read from the body's configuration; $t$ is the only variable.
Alongside $theta$ sit two bookkeeping quantities coupled by one invariant:

$ theta = theta_"dir" + theta_"inv" quad (mod 360degree). $ <eq-invariant>

- $theta_"dir"$ (`CelestialBody.directRotAngle`) is *per body*. It is the body's
  spin phase *as expressed in the game's world coordinates*: the heading of its
  prime meridian on screen.
- $theta_"inv"$ (`Planetarium.InverseRotAngle`) is a *single global scalar*. It is
  how far the world coordinate system itself has been turned.

The frames built from them are

$ Zup = Rz(theta_"inv") quad ("the planetarium frame"), wide Bf = Rz(theta_"dir") quad ("the body frame"). $

The relevant source is short enough to quote in full:

```csharp
rotationAngle = (initialRotation
                 + 360.0 * rotPeriodRecip * Planetarium.GetUniversalTime()) % 360.0;

if (!inverseRotation)
{
    directRotAngle = (rotationAngle - Planetarium.InverseRotAngle) % 360.0;
    Planetarium.CelestialFrame.PlanetaryFrame(
        0.0, 90.0, directRotAngle, ref BodyFrame);
    rotation = BodyFrame.Rotation.swizzle;
    bodyTransform.rotation = rotation;
}
else
{
    Planetarium.InverseRotAngle = (rotationAngle - directRotAngle) % 360.0;
    Planetarium.CelestialFrame.PlanetaryFrame(
        0.0, 90.0, Planetarium.InverseRotAngle, ref Planetarium.Zup);
    Planetarium.Rotation =
        QuaternionD.Inverse(Planetarium.Zup.Rotation).swizzle;
}
```

The switch changes only *which of the two is computed from the other*:

#align(center)[
  #table(
    columns: 3,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    table.header([*mode*], [*body frame* $Bf$], [*planetarium frame* $Zup$]),
    [inertial], [$Rz(theta_"dir")$, recomputed], [$Rz(theta_"inv")$, frozen],
    [rotating], [*never written at all*], [$Rz(theta_"inv")$, recomputed],
  )
]

Note the entry in bold. In the rotating branch the body frame is not written to the
same value; it is not written *at all*. The planet is frozen by omission,
wherever it last happened to be pointing.

== Why the transition costs nothing

#thm[Proposition 3.1 (Free continuity).][
  At the instant `inverseRotation` flips, neither $theta_"dir"$ nor $theta_"inv"$
  changes value. Hence both $Bf$ and $Zup$ are literally unchanged across the
  switch, *and this holds for any function used to build them from those two
  angles.*
]

#proofbox[
  No arithmetic is applied to *either angle* at the transition. Something else does
  happen on that tick, and @sec-real returns to it: `setRotatingFrame` rewrites every
  loaded rigidbody's velocity. But it leaves $theta_"dir"$ and $theta_"inv"$ alone,
  and those are the only inputs the frames have. On the tick before, $theta_"dir"$ was
  computed from @eq-invariant and $theta_"inv"$ left alone; on the tick after, the
  reverse. Both branches preserve @eq-invariant,
  and at the instant of the switch both variables hold the values they held a tick
  earlier. Since $Bf$ and $Zup$ are functions of those variables alone, they too are
  unchanged, whatever those functions happen to be.
]

The parenthetical is what the rest of the document relies on. Continuity at the
threshold is a property of the invariant @eq-invariant alone, not of the particular
frames stock chooses to build, so $Rz(dot)$ may be replaced by any frame-generating
function and continuity survives.

== The scalar subtraction is a change of coordinates

#thm[Proposition 3.2.][
  In stock KSP, $Bf = Zup^tr dot Rz(theta)$ holds identically, in both modes.
]

#proofbox[
  $Zup^tr Rz(theta) = Rz(-theta_"inv") Rz(theta) = Rz(theta - theta_"inv") = Rz(theta_"dir") = Bf$,
  using @eq-zcompose and @eq-invariant. The invariant holds in both branches, so
  the identity does too.
]

In the passive reading, *the $-theta_"inv"$ term is not a correction for something
happening elsewhere. It is the change of coordinates from the fixed celestial frame
into the turned world frame.* That is why every body
performs the subtraction, not just the one holding the rotating frame. The other
bodies are not being adjusted for a neighbour's behaviour; they are being
re-expressed in the coordinate system everything is drawn in.

Proposition 3.2 is the identity we will generalise. Everything that goes wrong when
tilt is introduced goes wrong because $Rz(-theta_"inv")$ is a scalar subtraction,
and a scalar subtraction is only a change of coordinates when both rotations share
an axis.

== The rotation is real, not notational <sec-real>

The passive reading of @sec-readings fits $Bf$ exactly. $Bf$ converts a body's
orientation from one set of coordinates to another, and converting coordinates
disturbs nothing.

It does not fit $Zup$. When $Zup$ changes, the game is not describing the same
arrangement of the solar system in different coordinates; it is putting the solar
system somewhere else. Four distinct mechanisms carry that motion into what the
player sees.

+ *The planet's transform stops.* As noted, the rotating branch never writes
  `BodyFrame`, `rotation`, or `bodyTransform.rotation`. The body's transform is
  therefore not advanced by this path at all, so the surface stops turning in world
  coordinates, which is the whole objective.

+ *Every body's world position is pushed through $Zup$.* Each `Orbit` position
  accessor ends in `Planetarium.Zup.WorldToLocal(...)`, i.e. multiplication by
  $Zup^tr$, and the result is written straight into a transform:

  ```csharp
  // Orbit.cs -- position from the elements, in the celestial frame
  Vector3d rCelestial = OrbitFrame.X * xPos + OrbitFrame.Y * yPos;
  return Planetarium.Zup.WorldToLocal(rCelestial);   // -> game world frame

  // OrbitDriver.cs -- and the setter writes bodyTransform.position
  celestialBody.position = referenceBody.position + pos;
  ```

+ *The sky is spun.* `GalaxyCubeControl` and `SkySphereControl` both set
  `transform.rotation = Planetarium.Rotation * initRot`, where
  `Planetarium.Rotation` is $Zup^(-1)$.

+ *Rigidbody velocities are rewritten at the switch.*
  `OrbitPhysicsManager.setRotatingFrame` walks every unpacked part and applies
  $bold(v) arrow.l bold(v) - bold(omega) times bold(r)$. A change of
  notation does not touch a physics engine.

Since every position picks up the same $Zup^tr$, the vector from a planet to its
moon is rotated too. With the planet's orientation frozen, the moon now circles it
once per planetary day, which is exactly what an observer on the ground sees
either way. Same observable, opposite implementation.

#thm[Consequence.][
  A rotation of $Zup$ about the wrong axis is not a cosmetic error. It moves every
  body's transform, the terrain system, and the skybox.
]

= Representing a tilt

== Poles, not Euler angles

*Reading a rotation matrix.* Recall from @sec-frames that a frame is stored as its
columns. That gives a way to read any rotation matrix at a glance: *column $k$ tells
you where the $k$-th reference axis ends up.* Formally, multiplying by $bold(e)_k$
selects the $k$-th column, so if $bold(M) = mat(bold(x), bold(y), bold(z))$ then
$bold(M) bold(e)_1 = bold(x)$, $bold(M) bold(e)_2 = bold(y)$ and
$bold(M) bold(e)_3 = bold(z)$.

Applied to a planet, the third column is the axis it spins about. That is the one
part of a body's orientation which does not change as it turns, so it is the natural
thing to store, and @eq-pole says `PlanetaryFrame` already accepts it directly.

*Right ascension and declination* are the sky's longitude and latitude, in that
order: declination is the latitude-like one. The declination $delta$ measures the
angle north of the celestial equator, running from
$-90degree$ to $+90degree$; the right ascension $alpha$ measures the angle around
that equator from a fixed reference direction, running from $0degree$ to
$360degree$. Any direction is fixed by the pair, and @eq-pole is exactly the
conversion from the pair to Cartesian components. So we store obliquity the way
astronomers do:

$ T := "PlanetaryFrame"(alpha, delta, 0), wide bold(p) := T bold(e)_3, wide epsilon := 90degree - delta, $ <eq-tilt>

where $alpha, delta$ are the right ascension and declination of the body's north
pole. In words: $T$ is the rotation that carries $bold(e)_3$ onto the pole
$bold(p)$, and $epsilon$ is the angle between the two.

For a concrete case, take $alpha = 0degree$ and $delta = 66.56degree$, so
$epsilon = 23.44degree$. Then @eq-pole gives

$ bold(p) = vec(cos 66.56degree dot cos 0degree, cos 66.56degree dot sin 0degree, sin 66.56degree) = vec(0.3977, 0, 0.9175), $

a unit vector leaning $23.44degree$ away from $bold(e)_3$ towards $bold(e)_1$. An
untilted body has $delta = 90degree$, which gives $bold(p) = bold(e)_3$ and
$T = bold(I)$: the rotation that moves nothing.

#thm[A caution about the word "obliquity".][
  *Throughout this document "obliquity" means the angle between the body's spin pole
  and the celestial $+z$ axis*, which is $epsilon = 90degree - delta$. It is not
  necessarily the conventional spin-orbit obliquity.
  Astronomers usually reserve *obliquity* for the angle between a body's pole and
  its own *orbit* normal. The two agree only when the reference plane is that orbit
  plane. They nearly do in the stock Kerbol system, where every planet orbits close
  to $z = 0$; they need not in a system built from real ICRF data, where a planet can
  have $delta = 90degree$ and hence $epsilon = 0$ here, while carrying a substantial
  true obliquity in the inclination of its orbit instead.
]

*Why a pole and not three Euler angles.* An Euler triple also determines an
orientation, but it determines it as a *composition*, leaving the pole implicit: to
answer "where does this body's axis point" you must multiply the three rotations out
first. Many different triples also give the same pole, so two configurations
describing the same physical tilt need not look alike. Storing the pole makes the
quantity every result below depends on immediate, and leaves exactly one degree of
freedom unaccounted for.

That leftover is the third parameter, the *prime meridian offset* $mu$, carried
alongside. A pole plus a spin angle is one parameterisation among many that give the
same pole, and they disagree about where longitude zero sits; $mu$ absorbs that
difference so a tilt converted from an older format reproduces the old body frame
exactly. We prove in @sec-pm that $mu$ cannot affect the planetarium frame.

== The conjugation lemma

The following lemma is used repeatedly below. Informally it says: *to rotate about an
awkward axis, carry the problem to where the axis is convenient, rotate there, and
carry it back.* If $bold(R)$ takes $bold(u)$ to $bold(R) bold(u)$, then $bold(R)^tr$
takes $bold(R) bold(u)$ back to $bold(u)$. Reading the product right to left, the
sandwich below undoes the carry, rotates by $theta$ about $bold(u)$ where that is
easy, then redoes it.

#thm[Lemma 4.1 (Conjugation).][
  Let $bold(R)$ be a rotation and let $bold(R)_bold(u)(theta)$ denote rotation by
  $theta$ about the unit axis $bold(u)$. Then
  $ bold(R) thin bold(R)_bold(u)(theta) thin bold(R)^tr = bold(R)_(bold(R) bold(u))(theta). $
]

*Sketch.* Write $bold(N) = bold(R) bold(R)_bold(u)(theta) bold(R)^tr$. It is a
rotation, and it fixes $bold(R) bold(u)$, so that is its axis. On any unit
$bold(w)$ perpendicular to $bold(u)$ it returns
$cos theta thin (bold(R) bold(w)) + sin theta thin (bold(R) bold(u) times bold(R) bold(w))$,
which is the planar rotation by $theta$ about $bold(R) bold(u)$ --- sign included, so
the sense is settled as well as the magnitude. The full argument is in
#link(<app-conjugation>)[Appendix B].

#thm[Corollary 4.2.][
  $T thin Rz(theta) thin T^tr$ is the rotation by $theta$ *about the body's own
  pole* $bold(p)$.
]

#proofbox[
  $Rz(theta) = bold(R)_(bold(e)_3)(theta)$, and $T bold(e)_3 = bold(p)$ by
  @eq-tilt. Apply Lemma 4.1.
]

This is the tool that lets us turn the sky about a tilted axis using only the
$R_z$-shaped generator KSP gives us.

= The construction <sec-construction>

*What has to be built.* @sec-what-moves described the arrangement stock maintains:
while a body holds the rotating frame the sky turns, the body stands still, and the
two are tied together closely enough that an observer on the ground cannot tell which
of the two implementations is running. We need that same arrangement for a body whose
pole is not $bold(e)_3$. Set out as requirements, the frames must satisfy:

+ the body's frame is *constant* for as long as the frame is held, a ground that
  moves defeating the entire purpose of the rotating frame;
+ the sky's rotation reproduces the apparent motion the body's own spin used to
  supply, which means turning about the body's pole $bold(p)$ rather than about
  $bold(e)_3$ (@sec-a8 shows what goes wrong otherwise);
+ both frames are continuous at the entry and at the exit, so that nothing jumps as
  the switch is thrown;
+ and every expression collapses to stock's own when $bold(p) = bold(e)_3$.

Stock meets all four with a single scalar $theta_"inv"$, and it can do so because
there the sky's axis and the body's axis are the same axis. Rotations about a common
axis compose by adding their angles (@eq-zcompose), so one number carries both. That
is precisely what fails once the pole leaves $bold(e)_3$: the two rotations no longer
share an axis, the angles no longer add, and what stock does with one number has to
be done with matrices.

*Why there is an anchor.* Reusing $theta_"inv"$ itself is the obvious economy, and it
comes close to working. It is a global accumulator that is never reset, so it holds
the total turn and $Rz(theta_"inv")$ is already an absolute answer rather than an
increment. Three things stop us. It is a scalar about $bold(e)_3$, which is the axis
we have just established is the wrong one. It is global while the pole is per body,
so its value means something different the moment the dominant body changes. And it
is maintained by code the mod does not control: Kopernicus sets `inverseRotation`
directly, skipping the update that keeps @eq-invariant true.

So the planetarium frame here is driven by *elapsed* rotation $e$, computed from the
body's own $theta$ and nothing else. That independence costs something. Elapsed
rotation is a relative quantity: it says how far the body has turned, not where it
started, and a frame needs both. The anchor supplies the missing half. In the product
$T thin Rz(e) thin T^tr thin Af$ the conjugated spin is the *how far* and $Af$ is the
*from where*, which is to say $Af$ is the constant of integration. Stock got its
equivalent for free by never resetting the accumulator.

#thm[Conventions for this section.][
  $theta$ is always the body's *absolute* spin phase from @eq-theta, never
  $theta_"dir"$. The symbol $e$ denotes *elapsed rotation*: how far the body has
  turned since it entered the rotating frame, so that $theta = theta_0 + e$ while the
  frame is held, where $theta_0$ is the phase at the instant of entry.
]

The construction is five objects, and the rest of this section proves they do what is
required:

$
T &= "PlanetaryFrame"(alpha, delta, 0)
  #h(1fr) & "(tilt frame, constant per body)" \
Lf(theta) &:= T thin Rz(theta + mu)
  #h(1fr) & "(local body frame)" \
Bf &:= Zup^tr thin Lf(theta)
  #h(1fr) & "(body frame, world coordinates)" \
Zup(e) &:= T thin Rz(e) thin T^tr thin Af
  #h(1fr) & "(planetarium frame)" \
Af &:= Lf(theta_0) thin bold(C)^tr
  #h(1fr) & "(anchor, latched on entry)"
$ <eq-defs>

The first line is @eq-tilt repeated, not a new definition: $T$ is fixed once per
body by its pole, with $T bold(e)_3 = bold(p)$, and $T = bold(I)$ when the body is
untilted. It carries no time dependence, so every occurrence of $T$ below is a
constant matrix, and $mu$ beside it is the prime meridian offset from the same
place. The one symbol not yet accounted for is $bold(C)$, the body frame the body
held at the instant of entry.

Compare these with what stock builds. $Lf$ is @eq-pole's factorisation with a real
pole instead of $bold(e)_3$; $Bf$ is Proposition 3.2 with the scalar subtraction
written out as an honest matrix; $Zup$ is a spin, but conjugated by $T$ so that
Corollary 4.2 applies. Line by line, stock's split of one *angle* into two has become
a split of one *rotation* into two:

#align(center)[
  #table(
    columns: 3,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, left, left),
    table.header([], [*stock, rotating branch*], [*this construction*]),
    [the sky], [$Zup = Rz(theta_"inv")$, advancing], [$Zup(e) = T Rz(e) T^tr Af$, advancing],
    [the body], [$Bf = Rz(theta_"dir")$, frozen], [$Bf = Zup^tr Lf(theta)$, constant],
    [the tie], [$theta = theta_"dir" + theta_"inv"$], [$Zup(e)^tr Lf(theta_0 + e)$ free of $e$],
    [the origin], [$theta_"inv"$ never resets], [$e$ resets on entry; $Af$ restores it],
  )
]

The right-hand column is the same idea carried out with matrices instead of a scalar,
which is what it takes once the two rotations no longer share an axis. Where stock
holds the body still by freezing a variable, @eq-defs holds it still by making $Bf$
come out constant, and that is the content of Theorem 5.1.

== The body frame is genuinely frozen

The rotating frame exists so that the ground does not move. Here is the proof that
it does not.

#thm[Theorem 5.1 (Frozen body).][
  With the definitions @eq-defs, $Bf$ is independent of $e$. Explicitly,
  $ Bf = Af^tr thin Lf(theta_0) quad "for all" e. $
]

#proofbox[
  During the rotating phase the spin phase is $theta = theta_0 + e$, so by
  @eq-zcompose
  $
  Lf(theta_0 + e) = T thin Rz(theta_0 + mu + e) = T thin Rz(theta_0 + mu) thin Rz(e) = Lf(theta_0) thin Rz(e).
  $
  Also $Zup(e)^tr = Af^tr thin T thin Rz(-e) thin T^tr$, since $T^tr = T^(-1)$ and
  the transpose reverses the product. Therefore
  $
  Bf(e) &= Zup(e)^tr thin Lf(theta_0 + e) \
        &= Af^tr thin T thin Rz(-e) thin T^tr dot Lf(theta_0) thin Rz(e) \
        &= Af^tr thin T thin Rz(-e) thin underbrace(T^tr thin T, bold(I)) thin Rz(theta_0 + mu) thin Rz(e)
          #h(1em) [#text(size: 9pt)[using $Lf(theta_0) = T Rz(theta_0 + mu)$]] \
        &= Af^tr thin T thin Rz(-e) thin Rz(theta_0 + mu) thin Rz(e) \
        &= Af^tr thin T thin Rz(theta_0 + mu) = Af^tr thin Lf(theta_0),
  $
  where the second-to-last step used the fact that rotations about a common axis
  commute and add, so $-e$ and $+e$ cancel. No $e$ remains.
]

Look at where the cancellation comes from. The body's own spin $Rz(e)$ appears on
the *right* of $Lf$; the conjugated sky rotation contributes $Rz(-e)$, and because
it sits *inside* the conjugation --- sandwiched between $T^tr$ and $T$ --- the $T$'s
annihilate and the two $R_z$'s meet. They meet only because they are rotations about
a common axis, which is what @eq-zcompose buys and what conjugating by $T$ was
arranged to preserve.

== Choosing the anchor

Theorem 5.1 says $Bf$ is *some* constant. Which constant is fixed entirely by
$Af$, and we get to choose it.

#thm[Corollary 5.2.][
  Setting $Af = Lf(theta_0) bold(C)^tr$ gives $Bf = bold(C)$ throughout the
  rotating phase.
]

#proofbox[
  By Theorem 5.1, $Bf = Af^tr Lf(theta_0)$. Substituting,
  $Af^tr = bold(C) thin Lf(theta_0)^tr$, so
  $Bf = bold(C) thin Lf(theta_0)^tr Lf(theta_0) = bold(C)$.
]

So the anchor is not a free parameter to be tuned: it is *derived* from the
orientation the body already has, and within @eq-defs it is the unique choice
that leaves the body
where it stands. In code:

```csharp
public static Planetarium.CelestialFrame AnchorFor(BodyTilt tilt, double rot,
    Planetarium.CelestialFrame current, Planetarium.CelestialFrame zup)
{
    if (!IsUsableRotation(current)) return OrIdentity(zup);

    var local = default(Planetarium.CelestialFrame);
    LocalBodyFrame(tilt, rot, ref local);

    return Multiply(local, Transpose(current));   //  L(rot) * transpose(C)
}
```

== The inertial case

Everything so far has been written from inside the rotating phase. The definitions
@eq-defs are not confined to it, and it is worth being explicit about what each of the
five becomes when the body is inertial, because the implementation evaluates them
through one unconditional path rather than two branches.

Three of the five do not vary with the mode at all. $T$ is fixed by the pole.
$Lf(theta)$ is built from $T$ and the spin phase, which by @eq-theta is a function of
time and nothing else, so it advances at the body's own rate either way. And
$Bf = Zup^tr Lf(theta)$ is the same product of the same two factors. What the mode
changes is not the formula but which factor is moving.

The remaining two *are* the mode. $Af$ does not exist: with no entry instant to latch
against there is no elapsed rotation to measure, and the anchor is released when the
body leaves (@sec-leaving). $Zup$ is not written by an inertial body, only by a
rotating one, so here it is external data. Two cases follow.

*Nothing holds the rotating frame.* No body writes $Zup$, so it is constant: the
identity in a fresh session, otherwise wherever the last rotating body left it.

#thm[Proposition 5.3 (Free rotation).][
  If $Zup$ is constant then $Bf(theta + Delta) = Bf(theta) thin Rz(Delta)$. The body
  turns by $Delta$ about its own pole, and the pole does not move.
]

#proofbox[
  $
  Bf(theta + Delta) = Zup^tr thin T thin Rz(theta + mu + Delta)
    = Zup^tr thin T thin Rz(theta + mu) thin Rz(Delta) = Bf(theta) thin Rz(Delta),
  $
  by @eq-zcompose. The factor is on the *right*, so by Proposition 2.3 it is a spin
  about the frame's own third column, which it therefore fixes.
]

Set that beside Theorem 5.1: the same product, the opposite conclusion. There a
$Rz(-e)$ arrived from the conjugated sky rotation and cancelled the body's $Rz(e)$;
here nothing arrives to cancel it and the body's own factor survives. The two results
are the two ways of supplying the same apparent motion, by turning the body or by
turning the sky, and in both the axis is $bold(p)$.

*Another body holds the rotating frame.* Only one body can, there being a single
`Planetarium.Zup` and a single dominant body to drive it. That body turns the sky for
every other, so an inertial body elsewhere sees a $Zup$ that moves, and
$Bf = Zup^tr Lf(theta)$ gives it both motions: its own spin about its own pole, and
the counter-turn every object picks up while the frame is held. The $Zup^tr$ factor is
easy to omit here, on the reasoning that a body which is not itself rotating is
unaffected by the switch. It is not: omitting the factor leaves it turning at its own
rate in a sky that has moved out from under it. Stock reaches the same place with a
scalar, $theta_"dir" = theta - theta_"inv"$ subtracting the same global turn, which is
Proposition 3.2.

#block(sticky: true)[Collected:]

#align(center)[
  #table(
    columns: 3,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, left, left),
    table.header([], [*inertial*], [*rotating*]),
    [$T$], [constant per body], [constant per body],
    [$Lf(theta)$], [advances with $theta$], [advances with $theta$],
    [$Af$], [none; $e$ undefined], [$Lf(theta_0) bold(C)^tr$, latched on entry],
    [$Zup$], [not written here; external], [$T Rz(e) T^tr Af$, advancing],
    [$Bf$], [$Zup^tr Lf(theta)$, advancing], [$Zup^tr Lf(theta)$, constant],
  )
]

The last row is the one to hold on to. $Bf$ is one expression, evaluated every tick in
both modes; whether the body appears to turn is settled entirely by whether the $Zup$
beside it is moving.

== Continuity at the threshold

#thm[Proposition 5.4.][
  Suppose the body was inertial immediately before the switch, so that its frame
  was $bold(C) = Zup_"prev"^tr thin Lf(theta_0)$ for the planetarium frame $Zup_"prev"$ in
  force at that moment. Then $Af = Zup_"prev"$, and $Zup(0) = Zup_"prev"$.
]

#proofbox[
  $
  Af = Lf(theta_0) bold(C)^tr = Lf(theta_0) (Zup_"prev"^tr Lf(theta_0))^tr
     = Lf(theta_0) Lf(theta_0)^tr Zup_"prev" = Zup_"prev".
  $
  And $Zup(0) = T thin Rz(0) thin T^tr Af = T T^tr Af = Af$.
]

Both frames are therefore continuous across the threshold, which is Proposition 3.1
realised for our particular choice of generator. Note that the anchor construction
is *also* correct when $bold(C)$ and $Zup_"prev"$ are inconsistent, since Corollary 5.2
does not assume they agree. That robustness matters: KSP has at least one code path
(`PSystemSetup.SetSpaceCentre`) that flips a body into the rotating frame and then
captures a floating-origin offset from its transform, at a moment when the body's
frame is stale by a whole game session's worth of rotation. Deriving the anchor from
$bold(C)$ rather than capturing $Zup$ makes that failure impossible by construction.

== Why the anchor multiplies on the right

#thm[Proposition 5.5.][
  Defining instead $tilde(Zup)(e) := Af thin T thin Rz(e) thin T^tr$ --- the anchor
  on the left --- does *not* freeze the body frame.
]

#proofbox[
  $tilde(Zup)(e)^tr = T thin Rz(-e) thin T^tr thin Af^tr$, so
  $
  tilde(Bf)(e) = T thin Rz(-e) thin T^tr thin Af^tr thin Lf(theta_0) thin Rz(e).
  $
  The factor $Af^tr Lf(theta_0)$ sits between $T^tr$ and $Rz(e)$, so $T^tr$ has
  nothing to cancel against and the two $Rz(plus.minus e)$ never meet. The expression
  therefore depends on $e$ unless $Af^tr Lf(theta_0)$ commutes with every $Rz(e)$,
  which happens exactly when it is itself a spin about $bold(e)_3$.

  That factor has a name. Substituting the anchor of Corollary 5.2,
  $
  Af^tr thin Lf(theta_0) = bold(C) thin Lf(theta_0)^tr thin Lf(theta_0) = bold(C),
  $
  so the left-anchored form freezes the body exactly when the frame it was latched
  against is a spin about $bold(e)_3$.
]

An untilted body satisfies that condition always, since then $Lf$ and $bold(C)$ are
both plain $R_z$ factors. So can a tilted one, if $bold(C)$ happens to come out as a
spin about $bold(e)_3$. The left anchor is therefore not always wrong; it is
conditionally right, on a condition that turns on where the body happened to be
pointing when the frame was taken. @eq-defs holds whatever $bold(C)$ is.

The placement is therefore forced by the requirement, not chosen for convenience.

== The prime meridian is invisible to the sky <sec-pm>

#thm[Proposition 5.6.][
  Folding $mu$ into $T$ --- replacing $T$ by $T thin Rz(mu)$ in the definition of
  $Zup$ --- changes nothing.
]

#proofbox[
  $
  (T Rz(mu)) thin Rz(e) thin (T Rz(mu))^tr
  = T thin Rz(mu) thin Rz(e) thin Rz(-mu) thin T^tr
  = T thin Rz(e) thin T^tr,
  $
  since rotations about $bold(e)_3$ commute by @eq-zcompose.
]

The sharper way to see it is through the axis rather than the algebra. By Lemma 4.1
each side is a rotation by $e$ about the image of $bold(e)_3$ under the conjugating
frame, and
$ (T thin Rz(mu)) bold(e)_3 = T bold(e)_3 = bold(p), $
so both conjugations turn about the very same axis. A spin about the pole cannot
move the pole, so it cannot change what the conjugation produces.

Geometrically: $mu$ is a spin about the pole, and a spin about an axis cannot move
that axis. Conjugation only ever cares where the axis went. The mod therefore stores
$mu$ separately and applies it only inside $Lf$, where it belongs.

== Reduction to stock

#thm[Proposition 5.7.][
  If $T = bold(I)$ and $mu = 0$, the definitions @eq-defs reduce to stock KSP's own
  *expressions*: $Bf = Rz(theta - theta_"inv")$ and $Zup = Rz(theta_"inv")$.
]

#proofbox[
  With $T = bold(I)$, $Lf(theta) = Rz(theta)$ and
  $Zup(e) = Rz(e) Af$. If the anchor was latched when the global angle was
  $theta_"inv"^0$, then $Af = Rz(theta_"inv"^0)$ and
  $Zup(e) = Rz(theta_"inv"^0 + e) = Rz(theta_"inv")$, because in stock
  $theta_"inv"$ advances by exactly the elapsed rotation while $theta_"dir"$ is
  frozen. Then
  $Bf = Zup^tr Lf(theta) = Rz(-theta_"inv") Rz(theta) = Rz(theta - theta_"inv")$.
]

One difference survives even at $T = bold(I)$. While a body holds the rotating frame
stock does not write `BodyFrame` at all, where @eq-defs recomputes it every tick.
Theorem 5.1 makes the recomputed value constant, so the two agree on what the frame
*is* while disagreeing on how often it is written. That is not a distinction without
a difference: anything that reads the body's orientation between the two writes, a
floating-origin offset taken from the body's transform above all, sees one version
where stock would have shown it the other.

Subject to that, this is why the patch is safe to leave enabled on an untilted
install: it is not
merely *close* to stock there, it is the same expression.

= Why the obvious approach fails, and why this one does not <sec-a8>

The natural first attempt is to keep stock's arithmetic and multiply a tilt onto
the result:

$ Bf_"naive" = T thin Rz(theta - theta_"inv") quad "versus" quad Bf = Rz(-theta_"inv") thin T thin Rz(theta). $

These differ, and the difference is instructive.

#thm[Proposition 6.1.][
  $Bf_"naive" = Bf$ for all $theta_"inv"$ if and only if $T$ commutes with every
  $R_z$, i.e. if and only if the pole lies on $plus.minus bold(e)_3$.
]

#proofbox[
  $Bf_"naive" = T Rz(-theta_"inv") Rz(theta)$ and $Bf = Rz(-theta_"inv") T Rz(theta)$.
  Cancelling the common right factor $Rz(theta)$, equality for all $theta_"inv"$ is
  exactly the statement that $T Rz(phi) = Rz(phi) T$ for all $phi$, i.e. that $T$
  commutes with the one-parameter group of rotations about $bold(e)_3$. A rotation
  commutes with all rotations about an axis precisely when it preserves that axis.
]

The failure has a clean geometric reading. Look at where each version puts the pole:

$ Bf_"naive" bold(e)_3 = T bold(e)_3 = bold(p), wide Bf bold(e)_3 = Rz(-theta_"inv") bold(p). $

The correct version turns the pole by $-theta_"inv"$, which is right: the entire
world frame was turned by $+theta_"inv"$, so in world coordinates the pole must
appear turned back by the same amount. The naive version leaves the pole exactly
where it was, because $Rz(theta - theta_"inv")$ multiplies on the *right*, and by
Proposition 2.3 a right factor is a spin about the pole, which can never move it.

#thm[Proposition 6.2 (Size of the error).][
  The angle between the two poles is
  $ Delta = 2 arcsin (sin epsilon dot abs(sin (theta_"inv" \/ 2))), $
  where $epsilon$ is the obliquity.
]

#proofbox[
  Both vectors lie on the cone of half-angle $epsilon$ about $bold(e)_3$, so both
  lie on a circle of radius $sin epsilon$. Rotating by $theta_"inv"$ moves a point
  of that circle by a chord of length $2 sin epsilon abs(sin(theta_"inv"\/2))$.
  For unit vectors, chord $c$ corresponds to angle $2 arcsin(c\/2)$.
]

Two things follow. First the magnitude: Kerbin's legacy tilt has
$epsilon = 20.591degree$, so at $theta_"inv" = 180degree$ the pole is misplaced by
$Delta = 41.2degree$. Second, and worse, $theta_"inv"$ *changes continuously* while
a body holds the rotating frame. The body's obliquity direction is therefore not
merely wrong but drifting: a planet whose axis wanders as a vessel flies around
it. In the game this showed up as an inverted season: a body reading its north pole
as leaning towards the Sun in its southern midsummer.

*Now put the same question to @eq-defs.* Where does it send the pole?

#thm[Corollary 6.3.][
  Under @eq-defs the body's pole in world coordinates is $Af^tr bold(p)$, which does
  not depend on the elapsed rotation $e$.
]

#proofbox[
  The pole in world coordinates is $Bf bold(e)_3$, and
  $Lf(theta) bold(e)_3 = T thin Rz(theta + mu) bold(e)_3 = T bold(e)_3 = bold(p)$,
  since $R_z$ fixes $bold(e)_3$. So $Bf bold(e)_3 = Zup^tr bold(p)$, and
  $
  Zup(e)^tr bold(p) = Af^tr thin T thin Rz(-e) thin T^tr bold(p)
  = Af^tr thin T thin Rz(-e) bold(e)_3 = Af^tr thin T bold(e)_3 = Af^tr bold(p),
  $
  using $T^tr bold(p) = bold(e)_3$ and then that $Rz(-e)$ fixes $bold(e)_3$. It also
  follows at once from Theorem 5.1, a constant $Bf$ having a constant third column;
  the value of doing it directly is the comparison below.
]

Three constructions, one question, three answers:

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, left),
    table.header([*construction*], [*pole in world coordinates*]),
    [naive, $Bf_"naive" = T Rz(theta - theta_"inv")$],
      [$bold(p)$, never moves, though the world frame turns],
    [stock's $Zup$ with a real pole],
      [$Rz(-theta_"inv") bold(p)$, dragged about the wrong axis, drifting],
    [@eq-defs], [$Af^tr bold(p)$, fixed for as long as the frame is held],
  )
]

The reason the third row works is physical rather than algebraic. While a body is
inertial its own spin supplies the sky's apparent motion, and a spin about an axis
cannot move that axis. When the rotating frame takes over, the sky has to reproduce
that motion exactly, so the sky's rotation must leave the axis alone too. Conjugating
by $T$ is what makes $R_z$ turn about $bold(p)$ instead of $bold(e)_3$, and by
Lemma 4.1 a rotation about $bold(p)$ fixes $bold(p)$. Stock satisfies the same
requirement, but only because every pole is $bold(e)_3$ there, so the two axes
coincide and the question never arises.

*The crossing, end to end.* A vessel descends past the threshold on a tilted planet.
`inverseRotation` flips, and nothing else changes on that tick (Proposition 3.1). The
anchor is latched as $Af = Lf(theta_0) bold(C)^tr$, which by Corollary 5.2 leaves the
body exactly where it stands and by Proposition 5.4 leaves the sky exactly where it
was. While the vessel remains below, the body's frame does not move at all
(Theorem 5.1), so the physics engine sees a stationary planet; the sky turns instead,
about the body's own pole, so the obliquity cannot drift (Corollary 6.3) and the
seasons and sub-solar point stay right however long the vessel stays down. On the way
back out the anchor is released and the body resumes from its own $theta$. If the
planet is untilted, every one of these expressions is stock's own (Proposition 5.7).

= Worked examples <sec-examples>

Everything so far has been symbolic. This section runs the construction on Kerbin's
own tilt and gives the numbers, so each result above can be checked against a value
rather than taken on the algebra alone.

All of them are produced by the shipped `TiltEmFrames`, not computed by hand, and can
be regenerated with

```text
dotnet test --filter WorkedExampleTests
```

Throughout, the body is Kerbin carrying an example tilt of $(20, 0, 5)$, its spin
phase at the moment of interest is $theta_0 = 137.4degree$, and the sky has already
been turned by $theta_"inv" = 100degree$ when the vessel drops below the threshold.

== The tilt frame

The tilt is given in the older of the two configuration formats, as Unity Euler angles
$(20, 0, 5)$. Nothing is built into the mod: every tilt is read from configuration, and
the example configuration that ships gives Kerbin a different figure, so the numbers
below stand on their own rather than describing a particular install. Converted to a
pole by `FromLegacyEuler`:

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, right),
    table.header([*quantity*], [*value*]),
    [$alpha$, right ascension of the pole], [$104.348568degree$],
    [$delta$, declination of the pole], [$69.409328degree$],
    [$mu$, prime meridian offset], [$-103.466390degree$],
    [$epsilon = 90degree - delta$, obliquity], [$20.590672degree$],
  )
]

and $T = "PlanetaryFrame"(alpha, delta, 0)$ comes out as

$
T = mat(
  -0.231989, -0.968806, -0.087156;
   0.906916, -0.247820,  0.340719;
  -0.351689,  0.000000,  0.936117
).
$ <eq-kerbinT>

The third column is the pole $bold(p)$, and @eq-pole reproduces it from $alpha$ and
$delta$ directly. The obliquity is the angle between that column and $bold(e)_3$:
$arccos(0.936117) = 20.590672degree$, agreeing with $90degree - delta$ as it must.

== Entering the rotating frame

Before the crossing the body is inertial, so $Zup = Rz(100degree)$ and the body frame
is $bold(C) = Zup^tr Lf(theta_0)$. In columns, the pole sits at

$
Lf(theta_0) bold(e)_3 = vec(-0.087156, 0.340719, 0.936117), wide
bold(C) bold(e)_3 = vec(0.350677, 0.026666, 0.936117).
$

Both have the same third component, since $Rz(-100degree)$ turns the pole about
$bold(e)_3$ and cannot change its height. Now latch the anchor,
$Af = Lf(theta_0) bold(C)^tr$:

$
Af = mat(
  -0.173648, -0.984808, 0;
   0.984808, -0.173648, 0;
   0,         0,        1
)
$

which is $Rz(100degree)$ exactly. That is Proposition 5.4 in numbers: the anchor comes
out equal to the planetarium frame already in force, so nothing moves at the crossing.
Measured as a rotation between the two frames, the difference is
$5.3 times 10^(-15)$ degrees, which is round-off.

== Holding the frame

Advance by $e = 90degree$ of elapsed rotation, a little over five hours of Kerbin's
day, and rebuild both frames:

#align(center)[
  #table(
    columns: 3,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, right, left),
    table.header([*quantity*], [*value*], [*by*]),
    [$Zup$ turned], [$90.000000degree$], [construction],
    [$Bf$ moved], [$1.6 times 10^(-14) degree$], [Theorem 5.1],
    [the pole moved], [$0degree$], [Corollary 6.3],
  )
]

The sky has gone round a quarter turn and the body has not moved at all. The pole
figure is exact rather than merely small: $Zup(e)^tr bold(p) = Af^tr bold(p)$
identically, so the two vectors are equal bit for bit and the angle between them is
$0$, not $10^(-14)$.

== Leaving the rotating frame <sec-leaving>

The vessel climbs back out. The flag clears, and three things follow from @eq-defs
without any further machinery.

Nothing writes $Zup$ any more, since only the rotating branch does, so the sky
freezes exactly where it stood. The anchor is dropped, so the next entry latches
afresh rather than resuming this one. And $Bf = Zup^tr Lf(theta)$ goes on being
rebuilt every tick with $Zup$ now constant, so the body starts turning again.

Continuing the same example, coast a further $60degree$ of Kerbin's rotation:

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, right),
    table.header([*quantity*], [*value*]),
    [$Bf$ at the exit tick, against $Bf(e)$], [$2.3 times 10^(-14) degree$],
    [$Zup$ drift over the coast], [$1.8 times 10^(-14) degree$],
    [$Bf$ turned], [$60.000000degree$],
    [the pole moved], [$0degree$],
  )
]

Leaving costs nothing, which is the easy direction: freezing a quantity is always
continuous, and the frames on either side of the exit agree to round-off. The body
then turns by exactly the $60degree$ its own spin phase advanced, and it turns
*about its own pole*, which does not move. That last row is the whole point of the
construction seen from the other side: in the rotating frame the sky went round the
pole while the body stood still, and out of it the body goes round the pole while the
sky stands still. The axis is the same one in both.

#thm[Why the anchor has to be dropped.][
  Suppose it were not, and the vessel came back below the threshold after that coast.
  The elapsed angle would be measured from the original latch, $theta - theta_0$,
  which has grown by the whole inertial arc, and $Zup$ would jump the moment the
  frame was re-entered:

  #align(center)[
    #table(
      columns: 2,
      stroke: 0.5pt + luma(70%),
      inset: 7pt,
      align: (left, right),
      [re-entry with the anchor released], [$2.1 times 10^(-14) degree$],
      [re-entry resuming the stale anchor], [$60.000000degree$],
    )
  ]

  The jump is the coast, exactly: $60degree$ of sky in one tick, carrying every
  on-rails vessel with it. Note that @eq-defs does not prevent this on its own: it
  says what the anchor *is* once latched, and dropping it at the exit is a separate
  obligation on the implementation. Note too the asymmetry, which is why the symptom
  is one-directional: entering can jump, leaving cannot.
]

== What the naive construction does instead

At the same instant, $Bf_"naive" = T thin Rz(theta_0 - theta_"inv")$ puts the pole at

$
Bf_"naive" bold(e)_3 = vec(-0.087156, 0.340719, 0.936117), wide
Bf bold(e)_3 = vec(0.350677, 0.026666, 0.936117),
$

$31.258274degree$ apart. Proposition 6.2 predicts

$ Delta = 2 arcsin(sin 20.590672degree dot abs(sin 50degree)) = 31.258274degree, $

to every digit printed. Note the left-hand vector: it is $Lf(theta_0) bold(e)_3$
unchanged, which is the failure exactly as @sec-a8 describes it. The naive pole never
notices that the world frame has turned.

At $theta_"inv" = 180degree$ the same formula gives $41.181343degree$, which is where
the "about $41degree$" of @sec-a8 comes from.

== The untilted case

Set $T = bold(I)$ and $mu = 0$, keeping everything else. Then $Bf = Zup^tr Lf(theta)$
should be stock's $Rz(theta_0 - theta_"inv") = Rz(37.4degree)$, and it is:

$
Bf bold(e)_1 = vec(0.794415, 0.607376, 0), wide
Rz(37.4degree) bold(e)_1 = vec(0.794415, 0.607376, 0),
$

with a largest component difference of $3.3 times 10^(-16)$, one unit in the last
place of a double. This is Proposition 5.7 measured rather than argued, and it is why
an untilted install is unaffected by the patch.

= Orbital elements <sec-elements>

The same algebra answers a quite different question: what inclination should a
player type in, and against what plane is it measured?

== The orbit frame

An orbit's orientation is described by three angles: the longitude of the ascending
node $Omega$, the inclination $i$, and the argument of periapsis $omega$. KSP builds
their frame with the same generator as everything else:

$ bold(O)(Omega, i, omega) = bold(S)(Omega, i, omega) = Rz(Omega) Rx(i) Rz(omega). $ <eq-orbitframe>

The columns have direct meaning. By @eq-zcol the third column is

$ bold(O) bold(e)_3 = vec(sin Omega sin i, -cos Omega sin i, cos i), $ <eq-orbitnormal>

the orbit normal --- the direction of the angular momentum vector --- and the first
column points at periapsis. The reference plane is the plane $z = 0$, so the
inclination $i$ in @eq-orbitnormal is measured from *the celestial equator*, and
$Omega$ from *the celestial $x$-axis*.

== Changing the reference plane

KSP measures every orbit against the celestial equator, the plane $z = 0$. For a
system built from real data that is a workable choice, since ephemeris services will
supply elements in that frame on request. They will supply them in others too:
JPL Horizons returns the same orbit against the ecliptic, against the ICRF equator, or
against a body's own equator, and the three disagree, so elements taken from one plane
and read against another put the orbit somewhere it never was. For an invented system
the celestial equator is simply a nuisance. What a
designer means by "a moon in my gas giant's equatorial plane" is a statement about
*the giant's* equator, and the celestial-frame numbers that express it are not round.

The fix is a single composition.

#thm[Proposition 8.1.][
  Let a parent body have tilt frame $T$. If $(Omega, i, omega)$ are read against the
  parent's equator, the corresponding celestial-frame elements are those of
  $ bold(O)_"cel" = T dot bold(O)(Omega, i, omega), $
  and conversely $bold(O)_"loc" = T^tr dot bold(O)_"cel"$.
]

#proofbox[
  The columns of $bold(O)(Omega,i,omega)$ are written in the parent's equatorial
  basis. By @eq-tilt, $T$ is precisely the matrix whose columns are that
  basis expressed in celestial coordinates, so $T$ applied to any set of
  parent-frame coordinates returns celestial coordinates. Applying it to each
  column of $bold(O)$ is matrix multiplication. The converse follows from
  $T^(-1) = T^tr$.
]

Because both frames come from the same generator @eq-zxz, the product is again a
frame of the same kind, and we can read elements back off it (@sec-decompose).

#thm[Remark.][
  The composition uses $T$, the pole *alone*, and not $T Rz(mu)$. The longitude of
  the ascending node is measured from an inertial reference direction; folding in
  the prime meridian would tie the orbit to wherever the parent's surface happens to
  put longitude zero, which has nothing to do with orbits.
]

The same idea rebases a *tilt* rather than an orbit: to give a moon a pole
expressed relative to its parent's, carry the local pole through the parent's frame
and re-read its right ascension and declination,

$ bold(p)_"cel" = T_"parent" bold(p)_"loc", quad delta = arcsin(p_3), quad alpha = op("atan2")(p_2, p_1). $

== Recovering elements from a frame <sec-decompose>

Inverting @eq-orbitframe means reading $Omega$, $i$ and $omega$ back off the matrix
entries. From @eq-zcol and the general form in Proposition 2.1,

$
bold(O)_3 = vec(sin Omega sin i, -cos Omega sin i, cos i),
wide
bold(O)_(1 3) = sin i sin omega, quad bold(O)_(2 3) = sin i cos omega,
$

where $bold(O)_(1 3)$ means the third component of the first column. Hence, when
$sin i eq.not 0$,

$
i = op("atan2")(sin i, cos i), quad
Omega = op("atan2")(bold(O)_(3 1), -bold(O)_(3 2)), quad
omega = op("atan2")(bold(O)_(1 3), bold(O)_(2 3)),
$ <eq-decompose>

with $sin i = sqrt(bold(O)_(3 1)^2 + bold(O)_(3 2)^2)$ taken from the first two
components of the third column. Section 8 explains why it must be computed that way
and not any other.

== The degenerate cases

#thm[Proposition 8.2.][
  If $i = 0$ then $bold(O) = Rz(Omega + omega)$; if $i = 180degree$ then
  $bold(O) = Rz(Omega - omega) Rx(180degree)$. In both cases $Omega$ and $omega$ are
  individually undetermined: only their sum, respectively their difference, is
  recoverable.
]

#proofbox[
  For $i = 0$, $Rx(0) = bold(I)$ and @eq-zcompose gives
  $bold(O) = Rz(Omega) Rz(omega) = Rz(Omega + omega)$. For $i = 180degree$, note
  that $Rx(180degree) = "diag"(1, -1, -1)$ satisfies
  $Rx(180degree) Rz(omega) = Rz(-omega) Rx(180degree)$, since conjugating a
  $z$-rotation by a half-turn about $x$ reverses it. Therefore
  $bold(O) = Rz(Omega) Rx(180degree) Rz(omega) = Rz(Omega - omega) Rx(180degree)$.
]

This is not a numerical artefact but a genuine geometric fact: an orbit lying in the
reference plane has no ascending node, so there is nothing for $Omega$ to measure.
The implementation resolves it by convention, setting $Omega = 0$ and putting the
whole angle into $omega$. With $Omega = 0$ the frame is $Rx(i) Rz(omega)$, whose
first column is $(cos omega, cos i sin omega, sin i sin omega)^tr$, so

$ omega = op("atan2")(bold(O)_(2 1) cos i, thin bold(O)_(1 1)). $ <eq-eqbranch>

The factor $cos i$ is doing real work: since $sin i = 0$ we have $cos^2 i = 1$, so
$bold(O)_(2 1) cos i = cos^2 i sin omega = sin omega$, and multiplying by
$cos i = plus.minus 1$ absorbs the sign flip between the prograde and retrograde
cases in one expression.

= Numerical conditioning <sec-numerics>

Everything above is exact arithmetic. In double precision two of the formulas are
traps, and each of them has caused a real bug in this implementation.

== `acos` cannot resolve a small angle

Suppose $c = cos i$ is known with absolute error $eta$. Differentiating
$i = arccos(c)$ gives

$ (dif i)/(dif c) = -1/sqrt(1 - c^2) = -1/(sin i), $

so the error in $i$ is about $eta \/ sin i$, which is unbounded as $i arrow 0$ or
$180degree$. Near $i = 180degree$ write $i = 180degree - beta$, with $beta$ in
radians; then $sin i approx beta$ and the error in the recovered angle is about
$eta \/ beta$. That error is small next to $beta$ itself only while
$beta >> sqrt(eta)$, and the two become equal at $beta = sqrt(eta)$ exactly. With
$eta approx 2 times 10^(-16)$ the floor is

$ sqrt(eta) approx 1.5 times 10^(-8) "radians", $ <eq-floor>

and below it `acos` does not resolve the angle at all: what it returns is no larger
than its own error. At $beta = 10^(-8)$ the error is already $2.2$ times the angle
being measured. Note where the loss occurs: $arccos(-1)$ is exactly $180degree$ in IEEE
arithmetic, so a frame that arrives with $bold(O)_(3 3)$ exactly $-1$ is fine. The
damage is done upstream. A frame that is *mathematically* polar but reaches the
decomposition as a product of other frames arrives with $bold(O)_(3 3)$ a little
above $-1$, and `acos` turns that into something like $179.999999degree$.

The two-argument $op("atan2")(sin i, cos i)$ has no such problem: it uses both
components, and near the poles the *sine* is the small, well-resolved quantity while
the cosine is order one.

== The cancellation that destroys the degeneracy test

Far worse is computing $sin i = sqrt(1 - c^2)$. Suppose the true frame is exactly
retrograde-equatorial, so $bold(O)_3 = (0, 0, -1)^tr$, but it arrived as a *product*
of frames --- as it does in Proposition 8.1 --- and carries rounding. Suppose that
rounding leaves $c = -1 + eta$; the exact $eta$ depends on the operands, but a couple
of units in the last place is typical, so take $eta tilde.op 2 times 10^(-16)$. Then

$ 1 - c^2 = 1 - (1 - 2 eta + eta^2) = 2 eta - eta^2 approx 4 times 10^(-16), $

and the square root returns

$ sqrt(1 - c^2) approx 2 times 10^(-8). $

The true value of $sin i$, computed from the first two components of the column, is
of order $10^(-16)$. *The subtraction has inflated the noise by eight orders of
magnitude*: the classic signature of catastrophic cancellation, where subtracting
two nearly equal quantities promotes rounding error into the leading digits.

Now trace the consequence. The implementation branches on whether the orbit is
equatorial:

```csharp
if (sinInc > 1e-12) { /* general case */ } else { /* equatorial convention */ }
```

With $sin i$ reported as $2 times 10^(-8)$, the near-degenerate frame takes the
*general* branch, and @eq-decompose recovers $Omega$ from
$op("atan2")$ of two components that are pure rounding noise. The node comes back
essentially at random. And by Proposition 8.2 that is not self-cancelling: for a
retrograde equatorial orbit only $Omega - omega$ is determined, so splitting it
wrongly between the two *moves the orbital plane*, by as much as $180degree$.

== The fix

Take $sin i$ from the column's own length in the equatorial plane, and the angle
from $op("atan2")$:

```csharp
var cosInc = Clamp(frame.Z.z, -1.0, 1.0);
var sinInc = Math.Sqrt(frame.Z.x * frame.Z.x + frame.Z.y * frame.Z.y);
elements.Inclination = Math.Atan2(sinInc, cosInc) * Rad2Deg;
```

Both quantities are now computed from data that is small when the answer is small.
No subtraction of nearly equal numbers occurs anywhere, the $10^(-12)$ gate becomes
meaningful again, and the degenerate branch is entered exactly when it should be.

#thm[Moral.][
  $sqrt(1 - cos^2 x)$ and $abs(sin x)$ are equal in $RR$ and are *not*
  interchangeable in floating point. Prefer the form whose inputs vanish with the
  output.
]

= Summary

The construction in one page.

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 8pt,
    align: (left, left),
    table.header([*object*], [*definition*]),
    [tilt frame], [$T = "PlanetaryFrame"(alpha, delta, 0)$, so $T bold(e)_3 = bold(p)$],
    [local body frame], [$Lf(theta) = T Rz(theta + mu)$ --- where the planet points, inertially],
    [body frame], [$Bf = Zup^tr Lf(theta)$ --- where it is drawn],
    [planetarium frame], [$Zup(e) = T Rz(e) T^tr Af$ --- a spin about $bold(p)$, by Cor. 4.2],
    [anchor], [$Af = Lf(theta_0) bold(C)^tr$ --- the unique choice freezing $Bf$],
    [element rebase], [$bold(O)_"cel" = T bold(O)_"loc"$, inverse $bold(O)_"loc" = T^tr bold(O)_"cel"$],
  )
]

The three results that make it work:

+ *Theorem 5.1* --- $Bf$ is independent of elapsed rotation, because the conjugation
  by $T$ puts the sky's $Rz(-e)$ and the body's $Rz(+e)$ on a common axis where they
  cancel.
+ *Proposition 3.1* --- continuity at the threshold is a property of the invariant
  $theta = theta_"dir" + theta_"inv"$, not of the generator, so any frame function
  inherits it.
+ *Proposition 5.7* --- with $T = bold(I)$ everything collapses to stock's own
  expressions exactly, so an untilted install is unaffected.

And the two traps:

+ *Proposition 6.1* --- you cannot bolt a tilt onto stock's scalar arithmetic,
  because $Rz(-theta_"inv")$ is only a change of coordinates when the two rotations
  share an axis.
+ *@sec-numerics* --- you cannot recover an inclination with `acos` and
  $sqrt(1 - c^2)$ near the degeneracies, because both destroy exactly the
  information the branch below them depends on.

#v(0.8cm)
#line(length: 100%, stroke: 0.5pt + luma(70%))
#v(0.3cm)

#heading(numbering: none, level: 1)[Appendix A: Notation]

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, left),
    table.header([*symbol*], [*meaning*]),
    [$bold(M)^tr$], [transpose; equals $bold(M)^(-1)$ for a frame],
    [$Rz(theta), Rx(theta)$], [elementary rotations, @eq-elementary],
    [$bold(S)(A,B,C)$], [the ZXZ generator $Rz(A) Rx(B) Rz(C)$, @eq-zxz],
    [$bold(e)_3$], [the reference $z$-axis, $(0,0,1)^tr$],
    [$t$], [universal time, seconds],
    [$P$], [`rotationPeriod`, the body's rotation period, seconds],
    [$theta$], [`rotationAngle`, the body's true spin phase, @eq-theta],
    [$theta_"dir"$], [`directRotAngle`, per body, the spin phase in world coordinates],
    [$theta_"inv"$], [`InverseRotAngle`, global, how far the world frame has turned],
    [$e$], [elapsed rotation since entering the rotating frame],
    [$alpha, delta$], [right ascension and declination of the pole],
    [$epsilon = 90degree - delta$], [obliquity],
    [$mu$], [prime meridian offset],
    [$T$], [the tilt frame, $"PlanetaryFrame"(alpha, delta, 0)$, @eq-tilt],
    [$bold(p) = T bold(e)_3$], [the body's north pole, in celestial coordinates],
    [$bold(C)$], [the body frame at the instant the rotating frame was entered],
    [$Omega, i, omega$], [longitude of ascending node, inclination, argument of periapsis],
  )
]

All angles in this document are in degrees, matching what KSP stores and what its
configuration files accept. In the source the split is: `PlanetaryFrame` and
`OrbitalFrame` take degrees and convert on entry, while the `SetFrame` they both call
takes radians.

#pagebreak(weak: true)
#heading(numbering: none, level: 1)[Appendix B: Proof of the conjugation lemma]
<app-conjugation>

Restating Lemma 4.1: for a rotation $bold(R)$ and a rotation
$bold(R)_bold(u)(theta)$ by $theta$ about the unit axis $bold(u)$,
$ bold(R) thin bold(R)_bold(u)(theta) thin bold(R)^tr = bold(R)_(bold(R) bold(u))(theta). $

Two standard facts about rotations are used in the proof. First, a rotation acts on
the plane perpendicular to its own axis as an ordinary planar rotation: for unit
$bold(u)$ and any unit $bold(w)$ perpendicular to it,

$ bold(R)_bold(u)(theta) bold(w) = cos theta thin bold(w) + sin theta thin (bold(u) times bold(w)), $ <eq-planar>

which is the familiar two-dimensional formula written in the orthonormal pair
$(bold(w), bold(u) times bold(w))$ spanning that plane. Second, a rotation preserves
cross products:

$ (bold(M) bold(a)) times (bold(M) bold(b)) = bold(M) (bold(a) times bold(b)) wide "for all" bold(M) in "SO"(3). $ <eq-crossprod>

Both sides are linear in $bold(a)$ and in $bold(b)$, so it suffices to check them on
basis vectors, where the statement reduces to the columns of $bold(M)$ forming a
right-handed triple. It is here that $det bold(M) = +1$ matters: a reflection would
flip the sign.

#proofbox[
  Write $bold(N) = bold(R) bold(R)_bold(u)(theta) bold(R)^tr$. First, $bold(N)$ is
  itself a rotation, since
  $
  bold(N) bold(N)^tr
  = bold(R) bold(R)_bold(u)(theta) underbrace(bold(R)^tr bold(R), bold(I)) bold(R)_bold(u)(theta)^tr bold(R)^tr
  = bold(R) underbrace(bold(R)_bold(u)(theta) bold(R)_bold(u)(theta)^tr, bold(I)) bold(R)^tr = bold(I),
  $
  and $det bold(N) = det bold(R) dot det bold(R)_bold(u)(theta) dot det bold(R)^tr = 1$.

  Now fix any unit $bold(w)$ perpendicular to $bold(u)$. Then
  $(bold(u), thin bold(w), thin bold(u) times bold(w))$ is a right-handed
  orthonormal basis, and since $bold(R)$ preserves lengths, angles and (by
  @eq-crossprod) cross products, so is
  $(bold(R) bold(u), thin bold(R) bold(w), thin bold(R) bold(u) times bold(R) bold(w))$.
  Two linear maps are equal when they agree on a basis, so it is enough to compare
  $bold(N)$ with $bold(R)_(bold(R) bold(u))(theta)$ on this one.

  *On the axis.* Using $bold(R)^tr bold(R) = bold(I)$ and the fact that $bold(u)$ is
  fixed by $bold(R)_bold(u)(theta)$,
  $
  bold(N) (bold(R) bold(u)) = bold(R) bold(R)_bold(u)(theta) bold(u) = bold(R) bold(u),
  $
  and $bold(R)_(bold(R) bold(u))(theta)$ fixes $bold(R) bold(u)$ by definition. Both
  agree.

  *On the perpendicular.* Applying @eq-planar and then @eq-crossprod,
  $
  bold(N)(bold(R) bold(w))
  &= bold(R) bold(R)_bold(u)(theta) bold(w)   &= bold(R) (cos theta thin bold(w) + sin theta thin (bold(u) times bold(w)))   &= cos theta thin (bold(R) bold(w)) + sin theta thin (bold(R) bold(u) times bold(R) bold(w)).
  $
  Since $bold(R) bold(w)$ is a unit vector perpendicular to $bold(R) bold(u)$, this
  is exactly what @eq-planar gives for $bold(R)_(bold(R) bold(u))(theta)$ applied to
  $bold(R) bold(w)$. Both agree, *and with the same sign on the $sin theta$ term*,
  which is what settles the sense of the rotation rather than merely its magnitude.

  *On the third basis vector.* Both maps are rotations, so both preserve cross
  products by @eq-crossprod. Their images of
  $bold(R) bold(u) times bold(R) bold(w)$ are therefore determined by their images
  of $bold(R) bold(u)$ and $bold(R) bold(w)$, which already agree.

  The two maps agree on a basis, hence are equal.
]

#heading(numbering: none, level: 1)[Appendix C: The swizzle]

KSP's celestial coordinates are right-handed with $z$ up; Unity's are left-handed
with $y$ up. Conversion swaps the last two components,

$ bold(S)_"w" = mat(1,0,0; 0,0,1; 0,1,0), wide bold(S)_"w" = bold(S)_"w"^tr = bold(S)_"w"^(-1), $

which is what `Vector3d.xzy` computes. Note $det bold(S)_"w" = -1$: the swap changes
handedness, which is exactly what is wanted, since the two conventions differ in
handedness too.

A frame is carried across by conjugation, $bold(M) arrow.r.bar bold(S)_"w" bold(M) bold(S)_"w"$,
which is the map `QuaternionD.swizzle` implements. Two properties matter:

+ It is a *homomorphism*:
  $bold(S)_"w" (bold(M) bold(N)) bold(S)_"w" = (bold(S)_"w" bold(M) bold(S)_"w")(bold(S)_"w" bold(N) bold(S)_"w")$,
  since $bold(S)_"w" bold(S)_"w" = bold(I)$. So *composition order is preserved*, and
  every identity proved in this document survives the conversion unchanged.
+ It preserves determinants, so rotations map to rotations despite
  $det bold(S)_"w" = -1$.

Consequently all of the algebra above may be done entirely in celestial
coordinates, converting only at the boundary where a value is handed to Unity.

#heading(numbering: none, level: 1)[Appendix D: Where this lives in the code]

#align(center)[
  #table(
    columns: 2,
    stroke: 0.5pt + luma(70%),
    inset: 7pt,
    align: (left, left),
    table.header([*result*], [*implementation*]),
    [@eq-tilt, Prop. 2.2], [`TiltEmFrames.FromPole`],
    [$Lf$, Prop. 2.3], [`TiltEmFrames.LocalBodyFrame`],
    [$Bf$, Prop. 3.2 generalised], [`TiltEmFrames.BodyFrame`],
    [$Zup$, Cor. 4.2], [`TiltEmFrames.Zup`],
    [$Af$, Cor. 5.2], [`TiltEmFrames.AnchorFor`],
    [Prop. 8.1], [`TiltEmFrames.ToCelestialElements`, `ToLocalElements`],
    [Pole rebase], [`TiltEmFrames.ToCelestialTilt`],
    [@sec-decompose, @sec-numerics], [`TiltEmFrames.DecomposeOrbitalFrame`],
  )
]
