<script>
$(document).ready(function () {
    // Check if there's a query parameter to open the modal
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('openChargeModal') === 'true') {
        // Open the charge request modal automatically
        $('#referAndEarn').modal('show');
        
        // Clean up URL without reloading
        if (window.history.replaceState) {
            const cleanUrl = window.location.protocol + "//" + window.location.host + window.location.pathname;
            window.history.replaceState({path: cleanUrl}, '', cleanUrl);
        }
    }

    $('#paymentrequestform').on('submit', function (e) {
        e.preventDefault();

        var formData = $(this).serialize();

        $.ajax({
            type: 'POST',
            url: '/Payments/ChargeRequest',
            data: formData,
            success: function (response) {
                Swal.fire({
                    title: 'درخواست شما با موفقیت ثبت شد',
                    text: 'همکاران ما در اسرع وقت به منظور تایید شارژ، با شما تماس خواهند گرفت. ',
                    confirmButtonText: 'متوجه شدم',
                    icon: 'success',
                    customClass: {
                        confirmButton: 'btn btn-primary waves-effect waves-light'
                    },
                    buttonsStyling: false
                });
                $('#paymentrequestform')[0].reset();
                $('#referAndEarn').modal('hide');
            },
            error: function (xhr, status, error) {
                $('#referAndEarn').modal('hide');
                Swal.fire({
                    icon: "error",
                    title: "مشکلی پیش آمده",
                    confirmButtonText: 'متوجه شدم',
                    text: "متاسفانه در ثبت پیام شما مشکلی پیش آمد می‌توانید با شماره 09902063015 تماس حاصل فرمایید.",
                    customClass: {
                        confirmButton: 'btn btn-primary waves-effect waves-light'
                    },
                    buttonsStyling: false
                });
            }
        });
    });
});
</script>
