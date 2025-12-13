/**
 * Reserve Trip Modals - Login and Insufficient Funds handling
 */

$(document).ready(function () {
    // Add custom regex method to jQuery Validate (for fallback)
    $.validator.addMethod("regex", function(value, element, regexp) {
        var re = new RegExp(regexp);
        return this.optional(element) || re.test(value);
    }, "لطفا یک مقدار معتبر وارد کنید");

    // Override unobtrusive validation adapter for regex
    $.validator.unobtrusive.adapters.add("regex", ["pattern"], function (options) {
        options.rules["regex"] = options.params.pattern;
        if (options.message) {
            options.messages["regex"] = options.message;
        }
    });

    // Handle form submission with authentication and balance checks
    $("form").on("submit", function(e) {
        e.preventDefault();
        
        // Use unobtrusive validation
        var validator = $(this).validate();
        if (!validator.valid()) {
            return false;
        }

        var formData = $(this).serialize();

        $.ajax({
            url: $(this).attr('action'),
            type: 'POST',
            data: formData,
            success: function(response) {
                // Check if authentication is required
                if (response && response.requiresAuth) {
                    showLoginModal(response.returnUrl);
                } 
                // Check if insufficient funds
                else if (response && response.insufficientFunds) {
                    showInsufficientFundsModal(response.requiredAmount, response.currentBalance);
                }
                else {
                    // If response is HTML (redirect happened), navigate to it
                    window.location.reload();
                }
            },
            error: function() {
                window.location.reload();
            }
        });
    });
});

// Function to show custom login modal
function showLoginModal(returnUrl) {
    var modal = `
        <div class="modal fade" id="loginRequiredModal" tabindex="-1" aria-hidden="true">
            <div class="modal-dialog modal-dialog-centered" role="document">
                <div class="modal-content" style="border-radius: 1.25rem; border: 1px solid rgb(230 232 238);">
                    <div class="modal-body p-4 text-center">
                        <div class="mb-3">
                            <svg xmlns="http://www.w3.org/2000/svg" width="80" height="80" viewBox="0 0 24 24" fill="none" stroke="#7367f0" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" class="mx-auto">
                                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"></rect>
                                <path d="M7 11V7a5 5 0 0 1 10 0v4"></path>
                            </svg>
                        </div>
                        <h4 class="mb-3" style="color: rgb(41 38 61);">برای ثبت بلیط ابتدا باید وارد اکانت خود شوید</h4>
                        <p class="text-muted mb-4">برای ادامه فرآیند رزرو بلیط، لطفا وارد حساب کاربری خود شوید</p>
                        <button type="button" class="btn btn-primary btn-lg w-100" id="confirmLoginBtn" style="border-radius: 1rem; height: 3.5rem;">
                            باشه
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;

    // Remove existing modal if any
    $('#loginRequiredModal').remove();
    
    // Add modal to body
    $('body').append(modal);
    
    // Show modal
    var modalElement = new bootstrap.Modal(document.getElementById('loginRequiredModal'));
    modalElement.show();

    // Handle confirm button click
    $('#confirmLoginBtn').on('click', function() {
        // Encode the return URL properly
        var loginUrl = '/Auth/Login?ReturnUrl=' + encodeURIComponent(returnUrl);
        window.location.href = loginUrl;
    });
}

// Function to show insufficient funds modal
function showInsufficientFundsModal(requiredAmount, currentBalance) {
    var modal = `
        <div class="modal fade" id="insufficientFundsModal" tabindex="-1" aria-hidden="true">
            <div class="modal-dialog modal-dialog-centered" role="document">
                <div class="modal-content" style="border-radius: 1.25rem; border: 1px solid rgb(230 232 238);">
                    <div class="modal-body p-4 text-center">
                        <div class="mb-3">
                            <svg xmlns="http://www.w3.org/2000/svg" width="80" height="80" viewBox="0 0 24 24" fill="none" stroke="#ff9f43" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" class="mx-auto">
                                <path d="M12.802 2.165l5.575 2.389c.48 .206 .863 .589 1.07 1.07l2.388 5.574c.22 .512 .22 1.092 0 1.604l-2.389 5.575c-.206 .48 -.589 .863 -1.07 1.07l-5.574 2.388c-.512 .22 -1.092 .22 -1.604 0l-5.575 -2.389a2.036 2.036 0 0 1 -1.07 -1.07l-2.388 -5.574a2.036 2.036 0 0 1 0 -1.604l2.389 -5.575c.206 -.48 .589 -.863 1.07 -1.07l5.574 -2.388a2.036 2.036 0 0 1 1.604 0z" />
                                <path d="M12 8v4" />
                                <path d="M12 16h.01" />
                            </svg>
                        </div>
                        <h4 class="mb-3" style="color: rgb(41 38 61);">موجودی حساب کافی نیست</h4>
                        <p class="text-muted mb-2">برای رزرو این بلیط، موجودی حساب آژانس شما کافی نمی‌باشد.</p>
                        ${requiredAmount ? '<p class="text-muted mb-1"><small>مبلغ مورد نیاز: <strong>' + requiredAmount.toLocaleString() + ' تومان</strong></small></p>' : ''}
                        ${currentBalance !== undefined ? '<p class="text-muted mb-4"><small>موجودی فعلی: <strong>' + currentBalance.toLocaleString() + ' تومان</strong></small></p>' : ''}
                        <button type="button" class="btn btn-warning btn-lg w-100" id="confirmChargeBtn" style="border-radius: 1rem; height: 3.5rem;">
                            <i class="ti ti-wallet me-2"></i>
                            افزایش موجودی
                        </button>
                        <button type="button" class="btn btn-label-secondary btn-lg w-100 mt-2" data-bs-dismiss="modal" style="border-radius: 1rem; height: 3rem;">
                            بستن
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;

    // Remove existing modal if any
    $('#insufficientFundsModal').remove();
    
    // Add modal to body
    $('body').append(modal);
    
    // Show modal
    var modalElement = new bootstrap.Modal(document.getElementById('insufficientFundsModal'));
    modalElement.show();

    // Handle confirm button click - redirect to agency index page with query parameter to open charge modal
    $('#confirmChargeBtn').on('click', function() {
        // Close current modal first
        modalElement.hide();
        // Redirect to Agency Index page with query parameter to automatically open the charge modal
        window.location.href = '/AgencyArea/Agency/Index?openChargeModal=true';
    });
}
