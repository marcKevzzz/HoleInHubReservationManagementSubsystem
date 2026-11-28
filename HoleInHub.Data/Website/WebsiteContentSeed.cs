namespace HoleInHub.Data.Website;

public static class WebsiteContentSeed
{
    public const string Json = """
        {
          "galleryCategories": [
            "Simulator Bays",
            "Venue & Lounge",
            "Promotions & Programs"
          ],
          "gallery": [
            {
              "category": "Promotions & Programs",
              "title": "Swing, Sip & Savor",
              "image": "/media/facebook/november-swing-sip-savor.jpg",
              "description": "Two hours of screen golf, a 15-minute basic lesson, one noodle dish, one side dish, and a bucket of five beers. Available from opening to closing in November. Reservations recommended.",
              "visibility": "Published",
              "id": "gallery-1",
              "promoPeriod": "current",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "November Promo",
              "image": "/media/facebook/november-promo-cover.jpg",
              "description": "Explore the November offers shown in the Gallery. More parking is available at the venue.",
              "visibility": "Published",
              "id": "gallery-2",
              "promoPeriod": "current",
              "compactPreview": true
            },
            {
              "category": "Promotions & Programs",
              "title": "Daytime Promo",
              "image": "/media/facebook/november-daytime-promo.jpg",
              "description": "Before 4 PM on weekdays, hourly rates are ₱400 for a Regular bay, ₱600 for a VIP room, and ₱1,000 for two VIP bays. Weekend hourly rates are ₱600, ₱800, and ₱1,500 respectively.",
              "visibility": "Published",
              "id": "gallery-3",
              "promoPeriod": "current",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "After 4 PM Offers",
              "image": "/media/facebook/november-after-4-pm.jpg",
              "description": "Weekdays and weekends after 4 PM. Get one free coffee per bay when you play for at least 1.5 hours. The one-hour golf and food offer includes one food item and costs ₱1,000 for Regular or ₱1,200 for VIP.",
              "visibility": "Published",
              "id": "gallery-4",
              "promoPeriod": "current",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "October Daytime Promo",
              "image": "/media/facebook/october-daytime-promo.webp",
              "description": "PHP 400 before 4 PM in October; excludes weekends and holidays.",
              "visibility": "Published",
              "id": "gallery-5",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "October Longest Drive Challenge",
              "image": "/media/facebook/october-longest-drive.webp",
              "description": "Ask staff to record and verify your drive for the October challenge.",
              "visibility": "Published",
              "id": "gallery-6",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "Third Junior Golf Program",
              "image": "/media/facebook/junior-golf-third.webp",
              "description": "Four lessons over one month. Recruitment deadline shown: October 2.",
              "visibility": "Published",
              "id": "gallery-7",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "KPGA Lesson Program",
              "image": "/media/facebook/kpga-lessons.webp",
              "description": "Small-group coaching with up to eight participants. Registration deadline shown: September 18.",
              "visibility": "Published",
              "id": "gallery-8",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "Second Junior Golf Program",
              "image": "/media/facebook/junior-golf-second.webp",
              "description": "A previous junior program announcement. Recruitment deadline shown: September 3.",
              "visibility": "Published",
              "id": "gallery-9",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Promotions & Programs",
              "title": "Get Coached by the Pros",
              "image": "/media/facebook/coaching-programs.webp",
              "description": "Philippine and KPGA professional coaching options.",
              "visibility": "Published",
              "id": "gallery-10",
              "captionCategory": "PROMOTIONS & PROGRAMS",
              "width": 526,
              "height": 526,
              "video": "",
              "promoPeriod": "past",
              "compactPreview": false
            },
            {
              "category": "Simulator Bays",
              "title": "Inside Hole In Hub",
              "image": "/media/facebook/venue-overview.webp",
              "description": "Open simulator bays, green hitting areas, and seating for time between swings.",
              "visibility": "Published",
              "id": "gallery-11",
              "captionCategory": "SIMULATOR BAYS",
              "width": 1200,
              "height": 1500,
              "video": "",
              "compactPreview": false
            },
            {
              "category": "Simulator Bays",
              "title": "A Round for the Family",
              "image": "/media/facebook/family-play.webp",
              "description": "Families sharing an indoor golf session at Hole In Hub.",
              "visibility": "Published",
              "id": "gallery-12",
              "captionCategory": "FAMILY PLAY",
              "width": 1200,
              "height": 900,
              "video": "",
              "compactPreview": false
            },
            {
              "category": "Simulator Bays",
              "title": "Learning the Swing",
              "image": "/media/facebook/coaching.webp",
              "description": "A hands-on moment helping a young player with their golf setup.",
              "visibility": "Published",
              "id": "gallery-13",
              "captionCategory": "COACHING",
              "width": 1200,
              "height": 1500,
              "video": "",
              "compactPreview": false
            },
            {
              "category": "Simulator Bays",
              "title": "Getting Ready to Play",
              "image": "/media/facebook/golf-experience-poster.webp",
              "description": "A short look at preparation for an indoor golf session.",
              "visibility": "Published",
              "id": "gallery-14",
              "captionCategory": "GOLF EXPERIENCE",
              "width": 720,
              "height": 1280,
              "video": "golf-experience-hq.mp4",
              "compactPreview": false
            },
            {
              "category": "Venue & Lounge",
              "title": "Golf and Good Company",
              "image": "/media/facebook/venue-tour-poster.webp",
              "description": "Take a short look around the venue and its social spaces.",
              "visibility": "Published",
              "id": "gallery-15",
              "captionCategory": "THE VENUE",
              "width": 720,
              "height": 1280,
              "video": "statement-loop-hd.mp4",
              "compactPreview": false
            },
            {
              "category": "Venue & Lounge",
              "title": "Between Swings",
              "image": "/media/facebook/social-seating.webp",
              "description": "Tables and seating alongside the simulator bays.",
              "visibility": "Published",
              "id": "gallery-16",
              "captionCategory": "SOCIAL SPACES",
              "width": 1200,
              "height": 1500,
              "video": "",
              "compactPreview": false
            },
            {
              "category": "Venue & Lounge",
              "title": "Golf and Snow Ice",
              "image": "/media/facebook/golf-and-desserts-poster.webp",
              "description": "Indoor golf and a look at the venue’s snow ice desserts.",
              "visibility": "Published",
              "id": "gallery-17",
              "captionCategory": "FOOD & DRINKS",
              "width": 720,
              "height": 1280,
              "video": "golf-and-desserts-hq.mp4",
              "compactPreview": false
            },
            {
              "category": "Venue & Lounge",
              "title": "Something Sweet",
              "image": "/media/facebook/dessert-menu.webp",
              "description": "The supplied menu features injeolmi, strawberry cheesecake, and Oreo snow ice. Menu details may change.",
              "visibility": "Published",
              "id": "gallery-18",
              "captionCategory": "FOOD & DRINKS",
              "width": 516,
              "height": 640,
              "video": "",
              "compactPreview": false
            }
          ],
          "offers": [
            {
              "category": "Rate Guide",
              "title": "Find the right time to play.",
              "image": "/media/facebook/offers-rates.jpg",
              "description": "Compare regular and VIP bay rates before you book.",
              "overlayTitle": "BAY RATES",
              "overlayDescription": "Flexible hourly rates for standard and VIP bays, with weekday and weekend options.",
              "buttonText": "VIEW SERVICES",
              "buttonDestination": "/Home/Services",
              "buttonVisibility": "visible",
              "visibility": "Published",
              "id": "offer-rates",
              "imageAlt": "Hole In Hub weekday, weekend, holiday, and VIP hourly bay rates.",
              "modalTitle": "Find the right time to play.",
              "modalDescription": "Flexible hourly rates for standard and VIP bays, with weekday and weekend options."
            },
            {
              "category": "Coaching",
              "title": "Build confidence with a coach.",
              "image": "/media/facebook/offers-coaching.jpg",
              "description": "Pick a single lesson or a multi-session package for focused practice.",
              "overlayTitle": "COACHING PACKAGES",
              "overlayDescription": "Choose a single lesson or a multi-session package to build your game with a coach.",
              "buttonText": "VIEW SERVICES",
              "buttonDestination": "/Home/Services",
              "buttonVisibility": "visible",
              "visibility": "Published",
              "id": "offer-coaching",
              "imageAlt": "KPGA coaching package options at Hole In Hub.",
              "modalTitle": "Build confidence with a coach.",
              "modalDescription": "Choose a single lesson or a multi-session package to build your game with a coach."
            },
            {
              "category": "Junior Golf",
              "title": "A fun first step into golf.",
              "image": "/media/facebook/junior-golf.jpg",
              "description": "A beginner-friendly introduction for kids ages six to thirteen.",
              "overlayTitle": "JUNIOR GOLF",
              "overlayDescription": "A four-week beginner golf program for kids ages six to thirteen. Ask about the next enrollment period.",
              "buttonText": "VIEW SERVICES",
              "buttonDestination": "/Home/Services",
              "buttonVisibility": "visible",
              "visibility": "Published",
              "id": "offer-junior-golf",
              "imageAlt": "Four-week beginner golf program for children ages six to thirteen.",
              "modalTitle": "A fun first step into golf.",
              "modalDescription": "A four-week beginner golf program for kids ages six to thirteen. Ask about the next enrollment period."
            }
          ],
          "section": {
            "subtitle": "More Ways to Play",
            "heading": "Offers & Coaching",
            "introduction": "Compare bay rates, explore coaching packages, or find a welcoming first golf program for kids. Open any card for a quick summary.",
            "visibility": "Published"
          }
        }
        """;
}
