﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Overlays.Rankings;
using osu.Game.Users;
using osu.Game.Rulesets;
using osu.Game.Online.Placeholders;
using osu.Game.Online.API;
using System.Threading;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Rankings.Tables;

namespace osu.Game.Overlays
{
    public class RankingsOverlay : FullscreenOverlay<RankingsOverlayHeader>, IOnlineComponent
    {
        protected Bindable<Country> Country => Header.Country;

        protected Bindable<RankingsScope> Scope => Header.Current;

        private readonly OverlayScrollContainer scrollFlow;
        private readonly Container contentContainer;
        private readonly LoadingLayer loading;
        private readonly Box background;

        private APIRequest lastRequest;
        private CancellationTokenSource cancellationToken;
        private Placeholder errorPlaceholder;
        private Container placeholderContainer;

        [Resolved]
        private IAPIProvider api { get; set; }

        public RankingsOverlay()
            : base(OverlayColourScheme.Green, new RankingsOverlayHeader
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Depth = -float.MaxValue
            })
        {
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both
                },
                scrollFlow = new OverlayScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            Header,
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Children = new Drawable[]
                                {
                                    contentContainer = new Container
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        AutoSizeAxes = Axes.Y,
                                        RelativeSizeAxes = Axes.X,
                                        Margin = new MarginPadding { Bottom = 10 }
                                    },
                                    loading = new LoadingLayer(contentContainer),
                                    placeholderContainer = new Container
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        AutoSizeAxes = Axes.Y,
                                        RelativeSizeAxes = Axes.X,
                                        Margin = new MarginPadding { Bottom = 10, Top = 200}
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            background.Colour = ColourProvider.Background5;
        }

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Header.Ruleset.BindTo(ruleset);

            Country.BindValueChanged(_ =>
            {
                // if a country is requested, force performance scope.
                if (Country.Value != null)
                    Scope.Value = RankingsScope.Performance;

                Scheduler.AddOnce(loadNewContent);
            });

            Scope.BindValueChanged(_ =>
            {
                // country filtering is only valid for performance scope.
                if (Scope.Value != RankingsScope.Performance)
                    Country.Value = null;

                Scheduler.AddOnce(loadNewContent);
            });

            ruleset.BindValueChanged(_ =>
            {
                if (Scope.Value == RankingsScope.Spotlights)
                    return;

                Scheduler.AddOnce(loadNewContent);
            });

            Scheduler.AddOnce(loadNewContent);
            api?.Register(this);
        }

        public void ShowCountry(Country requested)
        {
            if (requested == null)
                return;

            Show();

            Country.Value = requested;
        }

        public void ShowSpotlights()
        {
            Scope.Value = RankingsScope.Spotlights;
            Show();
        }

        private void loadNewContent()
        {
            loading.Show();

            cancellationToken?.Cancel();
            lastRequest?.Cancel();

            if (Scope.Value == RankingsScope.Spotlights)
            {
                loadContent(new SpotlightsLayout
                {
                    Ruleset = { BindTarget = ruleset }
                });
                return;
            }

            var request = createScopedRequest();
            lastRequest = request;

            if (request == null)
            {
                loadContent(null);
                return;
            }

            request.Success += () => Schedule(() => loadContent(createTableFromResponse(request)));
            request.Failure += _ => Schedule(() => loadContent(null));

            api.Queue(request);
            api?.Register(this);
        }

        private APIRequest createScopedRequest()
        {
            switch (Scope.Value)
            {
                case RankingsScope.Performance:
                    return new GetUserRankingsRequest(ruleset.Value, country: Country.Value?.FlagName);

                case RankingsScope.Country:
                    return new GetCountryRankingsRequest(ruleset.Value);

                case RankingsScope.Score:
                    return new GetUserRankingsRequest(ruleset.Value, UserRankingsType.Score);
            }

            return null;
        }

        private Drawable createTableFromResponse(APIRequest request)
        {
            switch (request)
            {
                case GetUserRankingsRequest userRequest:
                    switch (userRequest.Type)
                    {
                        case UserRankingsType.Performance:
                            return new PerformanceTable(1, userRequest.Result.Users);

                        case UserRankingsType.Score:
                            return new ScoresTable(1, userRequest.Result.Users);
                    }

                    return null;

                case GetCountryRankingsRequest countryRequest:
                    return new CountriesTable(1, countryRequest.Result.Countries);
            }

            return null;
        }

        private void checkIsLoggedIn()
        {
            //ask to log in if the user is not logged in
            if (api?.IsLoggedIn != true)
            {
                contentContainer.Hide();
                errorPlaceholder = new LoginPlaceholder(@"Please sign in to view ranking leaderboards");
                placeholderContainer.Child = errorPlaceholder;
                placeholderContainer.Show();
                loading.Hide();
            }
            else
            {
                placeholderContainer.Hide();
                contentContainer.Show();
            }
        }

        private void loadContent(Drawable content)
        {
            scrollFlow.ScrollToStart();

            if (content == null)
            {
                contentContainer.Clear();
                loading.Hide();
                return;
            }

            LoadComponentAsync(content, loaded =>
            {
                loading.Hide();
                contentContainer.Child = loaded;
            }, (cancellationToken = new CancellationTokenSource()).Token);

            api?.Register(this);
        }

        protected override void Dispose(bool isDisposing)
        {
            lastRequest?.Cancel();
            cancellationToken?.Cancel();

            base.Dispose(isDisposing);
        }

        public override void APIStateChanged(IAPIProvider api, APIState state)
        {
            checkIsLoggedIn();
        }
    }
}
