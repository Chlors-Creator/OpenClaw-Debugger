(function () {
  'use strict';

  window.OpenClawPluginCommunityState = {
    create() {
      return {
        items: [],
        loaded: false,
        loading: false,
        generation: 0,
        requestController: null,
        nextCursor: '',
        catalogRaw: null,
        catalogFormatError: false,
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
