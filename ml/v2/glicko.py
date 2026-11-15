"""Glicko-2 ratings (Glickman, "Example of the Glicko-2 system", 2013).

Each event is one rating period: every result on a card is rated together, against the
opponents' ratings from before the card. A fighter's deviation grows with the time since
their last fight, by sigma^2 per `period_days` of inactivity, before each event they're on.
"""

import math
from dataclasses import dataclass

SCALE = 173.7178  # Glicko-2's internal units per Glicko rating point
_EPS = 1e-6


@dataclass(frozen=True)
class GlickoParams:
    tau: float = 0.5           # how much volatility can change
    period_days: float = 30.0  # days of inactivity that add one sigma^2 to the deviation
    rating0: float = 1500.0
    rd0: float = 350.0
    sigma0: float = 0.06


class Glicko2:
    def __init__(self, params=GlickoParams()):
        self.p = params
        self.players = {}  # id -> [mu, phi, sigma, last date]

    def state(self, pid):
        """(rating, deviation) as of the end of the fighter's last rated fight."""
        mu, phi, _, _ = self.players.get(pid, self._new())
        return self.p.rating0 + SCALE * mu, SCALE * phi

    def expected(self, a, b, date):
        """P(a beats b) on a date, from both deviations inflated to that date."""
        mu_a, phi_a = self._before(a, date)
        mu_b, phi_b = self._before(b, date)
        return 1 / (1 + math.exp(-_g(math.hypot(phi_a, phi_b)) * (mu_a - mu_b)))

    def rate_event(self, date, games):
        """games: (a, b, score for a) for every rated result on one card."""
        if not games:
            return
        before = {pid: self._before(pid, date) for g in games for pid in g[:2]}
        opponents = {pid: [] for pid in before}
        for a, b, s in games:
            opponents[a].append((b, s))
            opponents[b].append((a, 1 - s))

        updated = {}
        for pid, games_of in opponents.items():
            mu, phi = before[pid]
            sigma = self.players.get(pid, self._new())[2]
            v_inv, delta_sum = 0.0, 0.0
            for opp, s in games_of:
                mu_j, phi_j = before[opp]
                g = _g(phi_j)
                e = 1 / (1 + math.exp(-g * (mu - mu_j)))
                v_inv += g * g * e * (1 - e)
                delta_sum += g * (s - e)
            v = 1 / v_inv
            sigma = self._volatility(phi, sigma, v, v * delta_sum)
            phi_star = math.sqrt(phi * phi + sigma * sigma)
            phi_new = min(1 / math.sqrt(1 / phi_star ** 2 + 1 / v), self.p.rd0 / SCALE)
            updated[pid] = [mu + phi_new ** 2 * delta_sum, phi_new, sigma, date]
        self.players.update(updated)

    def _new(self):
        return [0.0, self.p.rd0 / SCALE, self.p.sigma0, None]

    def _before(self, pid, date):
        """(mu, phi) going into a fight on `date`, with phi grown for the time out."""
        mu, phi, sigma, last = self.players.get(pid, self._new())
        if last is not None:
            periods = max((date - last).days, 0) / self.p.period_days
            phi = min(math.sqrt(phi * phi + sigma * sigma * periods), self.p.rd0 / SCALE)
        return mu, phi

    def _volatility(self, phi, sigma, v, delta):
        """Step 5 of the paper: the new volatility, by the Illinois algorithm."""
        a = math.log(sigma * sigma)
        tau = self.p.tau

        def f(x):
            ex = math.exp(x)
            return ex * (delta * delta - phi * phi - v - ex) / (2 * (phi * phi + v + ex) ** 2) - (x - a) / tau ** 2

        big_a = a
        if delta * delta > phi * phi + v:
            big_b = math.log(delta * delta - phi * phi - v)
        else:
            k = 1
            while f(a - k * tau) < 0:
                k += 1
            big_b = a - k * tau
        fa, fb = f(big_a), f(big_b)
        while abs(big_b - big_a) > _EPS:
            c = big_a + (big_a - big_b) * fa / (fb - fa)
            fc = f(c)
            if fc * fb <= 0:
                big_a, fa = big_b, fb
            else:
                fa /= 2
            big_b, fb = c, fc
        return math.exp(big_a / 2)


def _g(phi):
    return 1 / math.sqrt(1 + 3 * phi * phi / math.pi ** 2)
