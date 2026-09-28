---
title: MMA Stats
emoji: 🥊
colorFrom: blue
colorTo: indigo
sdk: docker
app_port: 7860
pinned: false
short_description: UFC fighters, events, and fight predictions
---

# MMA Stats

Every UFC fighter and event since UFC 1, with a prediction model that picks fights: upcoming bouts, recent ones (picked before they happened), and dream matchups between any two fighters.

The model is a logistic regression on each fighter's UFC history. On 1,300 fights it never saw, its favorite won about 60% of the time, and its percentages match how often favorites actually win. The site's "How It Works" page has the full results, including where it's no better than a simple rule.

The data is a snapshot, so "upcoming" means upcoming when it was collected; the footer shows the date.
