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

The model is a logistic regression on each fighter's UFC record, fight-by-fight striking and grappling stats, and age. On 1,300 fights it never saw, its favorite won about 64% of the time, ahead of simply backing the fighter with the better record (60%). The site's "How It Works" page has the full results, including the gradient boosting and neural network it was compared with.

The data is a snapshot, so "upcoming" means upcoming when it was collected; the footer shows the date.

Code: [github.com/reecemill/MMA-HUB](https://github.com/reecemill/MMA-HUB)
