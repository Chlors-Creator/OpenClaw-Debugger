(function () {
  'use strict';

  window.OpenClawSkillCommunityState = {
    create() {
      return {
        items: [], nextCursor: '', query: '', sort: 'recommended', loaded: false, loading: false,
        error: '', generation: 0, requestController: null, selectedItem: null, detail: null,
        detailLoading: false, detailGeneration: 0, detailController: null, installingSlug: ''
      };
    }
  };
})();
