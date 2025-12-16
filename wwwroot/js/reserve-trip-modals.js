/**
 * Reserve Trip Modals - Trip Information Modal
 */

$(document).ready(function () {
    // Show trip info modal on page load
    showTripInfoModal();

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

    // REMOVED: The problematic AJAX form handler that was preventing redirect
    // The payment form should submit normally to allow server redirect to Zarinpal
});

// Function to show trip information modal with animated map
function showTripInfoModal() {
    var modal = `
        <div class="modal fade" id="tripInfoModal" tabindex="-1" aria-hidden="true" data-bs-backdrop="static" data-bs-keyboard="false">
            <div class="modal-dialog modal-dialog-centered" role="document">
                <div class="modal-content" style="border-radius: 1.5rem; border: none; overflow: hidden; box-shadow: 0 20px 60px rgba(0,0,0,0.15);">
                    <!-- Map Image Header with Overlay -->
                    <div class="modal-header border-0 p-0" style="position: relative; height: 200px; overflow: hidden;">
                        <!-- Background Map Image -->
                        <div style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; background-image: url('/Gemini_Generated_Image_s4ahl2s4ahl2s4ah.png '); background-size: cover; background-position: center; opacity: 0.95;"></div>
                        
                        <!-- Overlay for better contrast -->
                        <div style="position: absolute; top: 0; left: 0; width: 100%; height: 100%; background: linear-gradient(to bottom, rgba(0,0,0,0.08), rgba(0,0,0,0.03));"></div>
                        
                        <!-- Route and Markers Overlay -->
                        <div class="w-100 h-100 d-flex align-items-center justify-content-center" style="position: relative; z-index: 2;">
                            <svg width="100%" height="100%" viewBox="0 0 200 200" preserveAspectRatio="xMidYMid meet" style="filter: drop-shadow(0 6px 12px rgba(0,0,0,0.25));">
                                <!-- Animated Route Path -->
                                <path d="M 35 170 Q 65 130, 100 105 T 165 35" 
                                      fill="none" 
                                      stroke="#5B8DEE" 
                                      stroke-width="6" 
                                      stroke-linecap="round"
                                      stroke-linejoin="round"
                                      opacity="0.85">
                                    <animate attributeName="stroke-width" 
                                             values="6;7;6" 
                                             dur="2s" 
                                             repeatCount="indefinite"/>
                                </path>
                                
                                <!-- Animated Dashed Overlay -->
                                <path d="M 35 170 Q 65 130, 100 105 T 165 35" 
                                      fill="none" 
                                      stroke="#ffffff" 
                                      stroke-width="3" 
                                      stroke-linecap="round"
                                      stroke-linejoin="round"
                                      stroke-dasharray="12,8"
                                      opacity="0.95">
                                    <animate attributeName="stroke-dashoffset" 
                                             from="0" 
                                             to="20" 
                                             dur="0.8s" 
                                             repeatCount="1"/>
                                </path>
                                
                                <!-- Origin Pin (Green - Google Maps Style) -->
                                <g class="origin-pin">
                                    <!-- Pin Shadow/Base -->
                                    <ellipse cx="35" cy="183" rx="14" ry="5" fill="#3ECF8E" opacity="0.4">
                                        <animate attributeName="rx" 
                                                 values="14;17;14" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.4;0.6;0.4" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                    </ellipse>
                                    
                                    <!-- Outer Pulse Ring -->
                                    <circle cx="35" cy="165" r="20" fill="none" stroke="#4CAF50" stroke-width="2" opacity="0">
                                        <animate attributeName="r" 
                                                 values="15;25;15" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.6;0;0.6" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="stroke-width" 
                                                 values="2;0;2" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                    </circle>
                                    
                                    <!-- Pin Body (Teardrop Shape) -->
                                    <path d="M 35 180 
                                             C 35 180, 25 172, 25 162
                                             C 25 156, 29 150, 35 150
                                             C 41 150, 45 156, 45 162
                                             C 45 172, 35 180, 35 180 Z" 
                                          fill="#4CAF50" 
                                          stroke="#ffffff" 
                                          stroke-width="2.5"
                                          filter="url(#pinShadow)">
                                        <animate attributeName="d" 
                                                 values="M 35 180 C 35 180, 25 172, 25 162 C 25 156, 29 150, 35 150 C 41 150, 45 156, 45 162 C 45 172, 35 180, 35 180 Z;
                                                         M 35 182 C 35 182, 24 173, 24 162 C 24 155, 28 148, 35 148 C 42 148, 46 155, 46 162 C 46 173, 35 182, 35 182 Z;
                                                         M 35 180 C 35 180, 25 172, 25 162 C 25 156, 29 150, 35 150 C 41 150, 45 156, 45 162 C 45 172, 35 180, 35 180 Z" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                    </path>
                                    
                                    <!-- Pin Inner Circle (White) -->
                                    <circle cx="35" cy="162" r="6" fill="#ffffff">
                                        <animate attributeName="r" 
                                                 values="6;7;6" 
                                                 dur="2s" 
                                                 repeatCount="indefinite"/>
                                    </circle>
                                </g>
                                
                                <!-- Destination Pin (Red - Google Maps Style) -->
                                <g class="destination-pin">
                                    <!-- Pin Shadow/Base -->
                                    <ellipse cx="165" cy="48" rx="14" ry="5" fill="#FF6B6B" opacity="0.4">
                                        <animate attributeName="rx" 
                                                 values="14;17;14" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.4;0.6;0.4" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                    </ellipse>
                                    
                                    <!-- Outer Pulse Ring -->
                                    <circle cx="165" cy="30" r="20" fill="none" stroke="#EA4335" stroke-width="2" opacity="0">
                                        <animate attributeName="r" 
                                                 values="15;25;15" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.6;0;0.6" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                        <animate attributeName="stroke-width" 
                                                 values="2;0;2" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                    </circle>
                                    
                                    <!-- Pin Body (Teardrop Shape) -->
                                    <path d="M 165 45 
                                             C 165 45, 155 37, 155 27
                                             C 155 21, 159 15, 165 15
                                             C 171 15, 175 21, 175 27
                                             C 175 37, 165 45, 165 45 Z" 
                                          fill="#EA4335" 
                                          stroke="#ffffff" 
                                          stroke-width="2.5"
                                          filter="url(#pinShadow)">
                                        <animate attributeName="d" 
                                                 values="M 165 45 C 165 45, 155 37, 155 27 C 155 21, 159 15, 165 15 C 171 15, 175 21, 175 27 C 175 37, 165 45, 165 45 Z;
                                                         M 165 47 C 165 47, 154 38, 154 27 C 154 20, 158 13, 165 13 C 172 13, 176 20, 176 27 C 176 38, 165 47, 165 47 Z;
                                                         M 165 45 C 165 45, 155 37, 155 27 C 155 21, 159 15, 165 15 C 171 15, 175 21, 175 27 C 175 37, 165 45, 165 45 Z" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                    </path>
                                    
                                    <!-- Pin Inner Circle (White) -->
                                    <circle cx="165" cy="27" r="6" fill="#ffffff">
                                        <animate attributeName="r" 
                                                 values="6;7;6" 
                                                 dur="2s" 
                                                 begin="0.5s"
                                                 repeatCount="indefinite"/>
                                    </circle>
                                </g>
                                
                                <!-- Animated Taxi Icon (Centered on Route) -->
                                <g class="taxi-icon">
                                    <!-- Gradient Definitions -->
                                    <defs>
                                        <linearGradient id="taxiGradient" x1="0%" y1="0%" x2="0%" y2="100%">
                                            <stop offset="0%" style="stop-color:#FFD54F;stop-opacity:1" />
                                            <stop offset="100%" style="stop-color:#FFC107;stop-opacity:1" />
                                        </linearGradient>
                                    </defs>
                                    
                                    <!-- Taxi Shadow (Centered) -->
                                    <ellipse cx="0" cy="13" rx="18" ry="4" fill="rgba(0,0,0,0.3)">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                    </ellipse>
                                    
                                    <!-- Taxi Sign on Roof -->
                                    <rect x="-8" y="-15" width="16" height="5" rx="2" fill="#FFE082" stroke="#F9A825" stroke-width="0.5">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.7;1;0.7" 
                                                 dur="0.8s" 
                                                 repeatCount="indefinite"/>
                                    </rect>
                                    
                                    <!-- Taxi Text on Sign -->
                                    <text x="0" y="-11" text-anchor="middle" font-size="4" font-weight="bold" fill="#F9A825">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                        TAXI
                                    </text>
                                    
                                    <!-- Main Taxi Body (Centered at 0,0) -->
                                    <rect x="-16" y="-8" width="32" height="16" rx="3" fill="url(#taxiGradient)" stroke="#F9A825" stroke-width="1.2">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                    </rect>
                                    
                                    <!-- Front Window -->
                                    <rect x="-14" y="-6" width="10" height="8" rx="1.5" fill="#34495E" opacity="0.7">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                    </rect>
                                    
                                    <!-- Back Window -->
                                    <rect x="4" y="-6" width="10" height="8" rx="1.5" fill="#34495E" opacity="0.7">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                    </rect>
                                    
                                    <!-- Front Headlight -->
                                    <circle cx="-17" cy="0" r="1.5" fill="#FFF8DC" opacity="0.9">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                        <animate attributeName="opacity" 
                                                 values="0.6;0.9;0.6" 
                                                 dur="1.2s" 
                                                 repeatCount="indefinite"/>
                                    </circle>
                                    
                                    <!-- Front Wheel (Centered) -->
                                    <g>
                                        <circle cx="-10" cy="9" r="4" fill="#2C3E50" stroke="#34495E" stroke-width="0.5">
                                            <animateMotion 
                                                path="M 35 170 Q 65 130, 100 105 T 165 35"
                                                dur="7s"
                                                repeatCount="indefinite"/>
                                        </circle>
                                        <circle cx="-10" cy="9" r="2" fill="#95A5A6">
                                            <animateMotion 
                                                path="M 35 170 Q 65 130, 100 105 T 165 35"
                                                dur="7s"
                                                repeatCount="indefinite"/>
                                        </circle>
                                    </g>
                                    
                                    <!-- Back Wheel (Centered) -->
                                    <g>
                                        <circle cx="10" cy="9" r="4" fill="#2C3E50" stroke="#34495E" stroke-width="0.5">
                                            <animateMotion 
                                                path="M 35 170 Q 65 130, 100 105 T 165 35"
                                                dur="7s"
                                                repeatCount="indefinite"/>
                                        </circle>
                                        <circle cx="10" cy="9" r="2" fill="#95A5A6">
                                            <animateMotion 
                                                path="M 35 170 Q 65 130, 100 105 T 165 35"
                                                dur="7s"
                                                repeatCount="indefinite"/>
                                        </circle>
                                    </g>
                                    
                                    <!-- Door Handle -->
                                    <rect x="-1" y="0" width="2" height="4" rx="0.5" fill="#F9A825" opacity="0.8">
                                        <animateMotion 
                                            path="M 35 170 Q 65 130, 100 105 T 165 35"
                                            dur="7s"
                                            repeatCount="indefinite"/>
                                    </rect>
                                </g>
                                
                                <!-- Filter Definitions -->
                                <defs>
                                    <filter id="pinShadow" x="-50%" y="-50%" width="200%" height="200%">
                                        <feGaussianBlur in="SourceAlpha" stdDeviation="2"/>
                                        <feOffset dx="0" dy="3" result="offsetblur"/>
                                        <feComponentTransfer>
                                            <feFuncA type="linear" slope="0.3"/>
                                        </feComponentTransfer>
                                        <feMerge>
                                            <feMergeNode/>
                                            <feMergeNode in="SourceGraphic"/>
                                        </feMerge>
                                    </filter>
                                </defs>
                            </svg>
                        </div>
                    </div>
                    
                    <!-- Modal Body -->
                    <div class="modal-body p-4 text-center">
                        <div class="mb-3">
                            <div class="d-inline-flex align-items-center justify-content-center rounded-circle mb-3" style="width: 60px; height: 60px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);">
                                <i class="ti ti-map-pin ti-lg text-white"></i>
                            </div>
                        </div>
                        <h4 class="mb-3" style="color: #2f3349; font-weight: 600;">
                            اطلاعات مبدا و مقصد
                        </h4>
                        <p class="text-muted mb-4" style="font-size: 1rem; line-height: 1.8;">
                            بعد از رزرو سفر، اطلاعات دقیق مبدا و مقصد از طریق <strong>نقشه تعاملی</strong> از شما دریافت می‌گردد تا راننده بتواند با دقت بیشتری به مقصد شما برسد.
                        </p>
                        
                        <!-- Info Cards -->
                        <div class="row g-3 mb-4">
                            <div class="col-6">
                                <div class="p-3 rounded-3" style="background: #f0f4ff; border: 1px solid #e0e7ff;">
                                    <i class="ti ti-map-2 ti-md text-primary mb-2"></i>
                                    <small class="d-block text-muted">انتخاب دقیق</small>
                                    <strong class="d-block" style="color: #667eea; font-size: 0.875rem;">مبدا</strong>
                                </div>
                            </div>
                            <div class="col-6">
                                <div class="p-3 rounded-3" style="background: #fff4f0; border: 1px solid #ffe7e0;">
                                    <i class="ti ti-flag ti-md text-danger mb-2"></i>
                                    <small class="d-block text-muted">انتخاب دقیق</small>
                                    <strong class="d-block" style="color: #764ba2; font-size: 0.875rem;">مقصد</strong>
                                </div>
                            </div>
                        </div>
                        
                        <button type="button" class="btn btn-primary btn-lg w-100 waves-effect waves-light" data-bs-dismiss="modal" style="border-radius: 1rem; height: 3.5rem; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); border: none; font-weight: 500;">
                            <i class="ti ti-check me-2"></i>
                            متوجه شدم، ادامه
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;

    // Remove existing modal if any
    $('#tripInfoModal').remove();
    
    // Add modal to body
    $('body').append(modal);
    
    // Show modal with fade animation
    var modalElement = new bootstrap.Modal(document.getElementById('tripInfoModal'), {
        backdrop: 'static',
        keyboard: false
    });
    
    // Small delay for smooth appearance
    setTimeout(function() {
        modalElement.show();
    }, 300);
}
