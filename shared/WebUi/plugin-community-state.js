(function () {
  'use strict';

  window.OpenClawPluginCommunityState = {
    create() {
      return {
        items: [],
        hotItems: [],
        loaded: false,
        loading: false,
        hotLoaded: false,
        hotLoading: false,
        hotError: '',
        hotGeneration: 0,
        hotRequestController: null,
        hotPeriod: 'all',
        generation: 0,
        requestController: null,
        nextCursor: '',
        catalogRaw: null,
        catalogFormatError: false,
        catalogError: '',
        query: '',
        sort: 'recommended',
        selectedItem: null,
        securityLoading: false,
        securityGeneration: 0,
        securityController: null,
        securityResult: null,
        securityError: ''
      };
    }
  };
})();
